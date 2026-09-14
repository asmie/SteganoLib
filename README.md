
# SteganoLib

![.NET](https://github.com/asmie/SteganoLib/actions/workflows/dotnet.yml/badge.svg)

## General Description

Library with steganographic algorithms to be used in .NET ecosystem. 

## Current state
Project is still under development. The library currently offers:
- image algorithms: keyed LSB matching with adaptive pixel selection and syndrome-trellis coding, and F5 for JPEG at the coefficient level;
- audio (WAV) LSB and phase coding, text steganography (zero-width, whitespace, homoglyphs), metadata carriers for PNG, JPEG and WAV, and per-frame video embedding for AVI and image sequences;
- an authenticated payload envelope, Reed-Solomon error correction, Shamir secret sharing across several carriers;
- steganalysis (chi-square, RS, sample pair) and distortion metrics (PSNR, SSIM, SNR);
- the `stegano` command-line tool below.

## Command-line tool

`SteganoLib.Cli` builds the `stegano` tool. Run it from the repository with `dotnet run --project SteganoLib.Cli --`, or pack and install it with `dotnet pack SteganoLib.Cli` and `dotnet tool install --global --add-source SteganoLib.Cli/bin/Release SteganoLib.Cli`.

```
stegano keygen --out key.bin
stegano embed   --in cover.png  --out stego.png --message "hello" --key-file key.bin
stegano extract --in stego.png  --as-text --key-file key.bin
stegano embed   --in cover.jpg  --out stego.jpg --data secret.bin --passphrase "correct horse" --ecc 32
stegano extract --in stego.jpg  --out secret.bin --passphrase "correct horse" --ecc 32
stegano capacity --in cover.wav
stegano analyze --in suspect.png
stegano compare --cover cover.png --stego stego.png
```

The carrier type follows the file extension: PNG, BMP and TIFF use LSB matching in keyed pixel order, JPEG uses F5, WAV uses sample LSB, and text files use zero-width characters (`--text-method whitespace` or `homoglyph` for the alternatives). `--carrier metadata` hides the payload in a PNG chunk, JPEG APP segment or WAV chunk instead of the signal. Every payload is sealed with the key, so extraction reports whether a payload was found and whether it authenticates. Exit code 0 means success, 1 a failed operation such as a wrong key, and 2 a usage error.

## Benchmarks

`SteganoLib.Benchmarks` holds BenchmarkDotNet benchmarks for the LSB, JPEG, coding, envelope and steganalysis paths. They are not part of the test run; execute them with

```
dotnet run -c Release --project SteganoLib.Benchmarks -- --filter '*'
```

and add `--job short` for a quick pass or a class name such as `*Lsb*` to the filter. On a 1024x768 cover the keyed pixel permutation (an 8-round Feistel network with SipHash-2-4) accounts for most of the LSB embedding time; pixel access is negligible.

## Compilation

### Prerequisites

Library demands:
* .NET compiler (like built-in Visual Studio - the project is included in repo);
* ImageSharp library;
* xUnit.

### Building

Simply build from the Visual Studio or use any other .NET compiler of your choice.

SteganoLib targets .NET 10.


## Bug reporting

Bugs can be reported using [GitHub issue tracker](https://github.com/asmie/SteganoLib/issues).

## Further development
Project is currently under development and therefore there will be more algorithms and formats.

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

Please make sure to update tests as appropriate.

## Versioning

I use [SemVer](http://semver.org/) for versioning. For the versions available, see the [tags on this repository](https://github.com/asmie/SteganoLib). 

## Authors

* **Piotr Olszewski** - *Original work* - [asmie](https://github.com/asmie)

See also the list of [contributors](https://github.com/asmie/SteganoLib/contributors) who participated in this project.


## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details
