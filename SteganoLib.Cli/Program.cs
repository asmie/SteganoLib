using System;
using System.CommandLine;
using System.IO;
using System.Text;

namespace SteganoLib.Cli
{
    /// <summary>Entry point of the <c>stegano</c> tool.</summary>
    public static class Program
    {
        public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        /// <summary>Run the tool with explicit writers so tests can capture the output.</summary>
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));

            var root = BuildCommands(new Operations(output), error);
            var configuration = new InvocationConfiguration { Output = output, Error = error };
            return root.Parse(args).Invoke(configuration);
        }

        private static RootCommand BuildCommands(Operations operations, TextWriter error)
        {
            var root = new RootCommand("Hide and recover authenticated payloads in PNG, JPEG, WAV and text files.");

            var input = new Option<string>("--in", "-i") { Description = "Carrier file to read.", Required = true };
            var output = new Option<string>("--out", "-o") { Description = "File to write." };
            var carrier = new Option<string>("--carrier", "-c") { Description = "auto (default), image, jpeg, wav, text or metadata." };
            var textMethod = new Option<string>("--text-method") { Description = "For text carriers: zero-width (default), whitespace or homoglyph." };
            var passphrase = new Option<string>("--passphrase", "-p") { Description = "Passphrase the key is derived from." };
            var keyFile = new Option<string>("--key-file", "-k") { Description = "File holding raw key bytes; see keygen." };
            var ecc = new Option<int>("--ecc") { Description = "Reed-Solomon parity bytes per 255-byte block, 0 for none.", DefaultValueFactory = _ => 0 };
            var compress = new Option<bool>("--compress") { Description = "Compress the payload before sealing when that makes it smaller." };

            CarrierOptions ReadCarrierOptions(ParseResult result) => new()
            {
                Input = result.GetValue(input),
                Kind = CarrierKinds.ParseKind(result.GetValue(carrier)),
                TextMethod = CarrierKinds.ParseTextMethod(result.GetValue(textMethod)),
                ErrorCorrection = result.GetValue(ecc),
                Compress = result.GetValue(compress),
                Passphrase = result.GetValue(passphrase),
                KeyFile = result.GetValue(keyFile),
            };

            var keygen = new Command("keygen", "Generate a random 32-byte key.");
            keygen.Options.Add(output);
            keygen.SetAction(result => Guard(error, () => operations.KeyGen(result.GetValue(output))));

            var dataFile = new Option<string>("--data", "-d") { Description = "File whose bytes are the payload." };
            var message = new Option<string>("--message", "-m") { Description = "Text payload, stored as UTF-8." };
            var embed = new Command("embed", "Seal a payload with the key and hide it in a carrier.");
            foreach (var option in new Option[] { input, output, dataFile, message, carrier, textMethod, passphrase, keyFile, ecc, compress })
                embed.Options.Add(option);
            embed.SetAction(result => Guard(error, () =>
            {
                string file = result.GetValue(dataFile);
                string text = result.GetValue(message);
                if ((file == null) == (text == null))
                    throw new CliException("Pass exactly one of --data or --message.", ExitCodes.Usage);
                if (file != null && !File.Exists(file))
                    throw new CliException($"File not found: {file}", ExitCodes.Usage);
                byte[] payload = file != null ? File.ReadAllBytes(file) : Encoding.UTF8.GetBytes(text);
                return operations.Embed(ReadCarrierOptions(result), result.GetValue(output), payload);
            }));

            var asText = new Option<bool>("--as-text") { Description = "Print the payload as UTF-8 text instead of saving it." };
            var extract = new Command("extract", "Recover and verify a payload hidden with embed.");
            foreach (var option in new Option[] { input, output, asText, carrier, textMethod, passphrase, keyFile, ecc })
                extract.Options.Add(option);
            extract.SetAction(result => Guard(error, () => operations.Extract(ReadCarrierOptions(result), result.GetValue(output), result.GetValue(asText))));

            var capacity = new Command("capacity", "Show how many payload bytes a carrier can hold.");
            foreach (var option in new Option[] { input, carrier, textMethod, passphrase, keyFile, ecc })
                capacity.Options.Add(option);
            capacity.SetAction(result => Guard(error, () => operations.Capacity(ReadCarrierOptions(result))));

            var analyze = new Command("analyze", "Run chi-square, RS and sample pair steganalysis on an image.");
            analyze.Options.Add(input);
            analyze.SetAction(result => Guard(error, () => operations.Analyze(result.GetValue(input))));

            var coverPath = new Option<string>("--cover") { Description = "Original file.", Required = true };
            var stegoPath = new Option<string>("--stego") { Description = "Modified file.", Required = true };
            var compare = new Command("compare", "Measure the distortion between a cover and a stego file.");
            compare.Options.Add(coverPath);
            compare.Options.Add(stegoPath);
            compare.SetAction(result => Guard(error, () => operations.Compare(result.GetValue(coverPath), result.GetValue(stegoPath))));

            foreach (var command in new[] { keygen, embed, extract, capacity, analyze, compare })
                root.Subcommands.Add(command);
            return root;
        }

        /// <summary>Turn exceptions into messages on the error stream and exit codes.</summary>
        private static int Guard(TextWriter error, Func<int> action)
        {
            try
            {
                return action();
            }
            catch (CliException e)
            {
                error.WriteLine($"error: {e.Message}");
                return e.ExitCode;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is NotSupportedException || e is ArgumentException || e is InvalidOperationException)
            {
                error.WriteLine($"error: {e.Message}");
                return ExitCodes.Failure;
            }
        }
    }
}
