
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

## Installation

The library is published on NuGet as `SteganoLib` and the command-line tool as `SteganoLib.Cli`:

```
dotnet add package SteganoLib
dotnet tool install --global SteganoLib.Cli
```

## Samples

`samples/SteganoLib.Samples` is a runnable tour: an authenticated message in a PNG with a quality report, F5 in a JPEG, zero-width text, a metadata chunk, Reed-Solomon repair of a damaged file, a payload shared across three images, and steganalysis of LSB replacement versus matching. It builds its own covers, so it needs no input files:

```
dotnet run --project samples/SteganoLib.Samples -- ./sample-output
```

The test suite runs the samples too, so they cannot drift from the API.

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

Image payloads must be saved as PNG, BMP or TIFF (`.png`, `.bmp`, `.tif`, `.tiff`). The CLI and image file helpers reject other output extensions before writing. They use explicit RGB-preserving encoder settings, including for palette and grayscale input images. PNG and BMP outputs retain alpha; TIFF output stores RGB. JPEG carriers use F5 and are handled separately. CLI image distortion reports compare the cover with the saved output.

## Capacity and framing

`StegoPipeline.Capacity(carrier)` reports the payload budget after envelope overhead, without compression. `IsPossibleToEmbed(length, carrier)` checks that length plus framing; a zero capacity alone does not establish whether even an empty authenticated payload fits. Error correction, sharing and video also check the framing requirements of their wrapped algorithms.

With `PayloadEnvelope.Compress = true` (CLI: `--compress`), embedding checks the actual sealed size, so compressible messages can exceed the reported uncompressed budget. A false length-only check does not rule out such messages. Pipeline capacity exceptions report the sealed payload size and the wrapped algorithm's capacity. Algorithms with payload-dependent constraints, such as forbidden trellis changes, may still reject a message within their length budget.

For phase coding, recordings shorter than 64 samples per channel, or shorter than the configured `SegmentLength`, have zero capacity. A segment of 64 samples cannot hold the length header either. On these carriers, raw extraction returns an empty array and embedding an empty raw payload leaves the audio unchanged; nonempty payloads and authenticated envelopes are rejected.

Custom pixel and sample selectors may return subsets. Their sequences must be finite, repeatable, in bounds and free of duplicates. `Count` must match the sequence length; its default implementation enumerates the selection, while built-in keyed selectors count without traversal. Content-aware pixel selectors use the image-based `Pixels` and `Count` overloads. Adaptive selectors can wrap other content-aware selectors and preserve the strictest required high bits; their own variance score always uses six-bit grey levels.

Trellis capacity is a length budget, not a promise that every payload can be embedded under the configured costs. Positive infinity forbids a payload change, and infeasible embedding throws `CapacityExceededException` even when `Required <= Available`. The plain algorithm header ignores cost models. F5 additionally forbids changes to magnitude-one payload coefficients because shrinking them would break extraction; this restriction applies to custom cost models too. F5's default matrix mode can fall back to one bit per coefficient to fit its conservative budget. These changes retain the existing header format, although new F5 trellis embeddings may choose a different width.

## JPEG input validation

JPEG coefficient loading checks segment boundaries, table and component references, Huffman code trees, sequential scan parameters, restart order and scan padding. These checks follow [ITU-T T.81, Annexes B, C and F](https://www.w3.org/Graphics/JPEG/itu-t81.pdf). Truncated scans are rejected with `InvalidDataException`; the decoder no longer supplies zero bits to finish them. Valid marker fill bytes and separate component scans remain supported.

Redefining a quantisation table after a component has used it is currently unsupported and throws `NotSupportedException`, because the in-memory representation cannot preserve different versions of the same table. This avoids silently changing pixels when saving. F5 extraction also checks the full coefficient requirement of matrix groups and trellis widths before allocating payload buffers.

## RIFF input validation

WAV, AVI and WAV metadata readers require complete RIFF containers, including chunk headers, payloads, padding and nested list boundaries. Truncated chunks and trailing bytes outside the declared container now raise `InvalidDataException`. The shared checks use the [RIFF chunk layout](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference).

PCM loading also requires consistent byte rate and sample alignment, a single format chunk before a single data chunk, and complete sample frames. Extensible PCM requires the full PCM subformat GUID; sample precision smaller than the container width is currently unsupported. WAV metadata editing preserves audio bytes without interpreting their encoding, so float and compressed formats remain usable as metadata carriers.

AVI loading validates dimensions, bitmap headers, frame timing and RGB frame sizes. MJPEG content and dimensions are checked when decoding each frame. Record lists are traversed iteratively. Multiple video streams, zero-length dropped frames, movie lists other than `rec `, and OpenDML extensions are explicitly unsupported.

## Metadata input validation

PNG metadata loading validates chunk boundaries, CRCs, the image header, palette placement, consecutive image-data chunks and the final empty `IEND`, following the [PNG specification](https://www.w3.org/TR/png-3/). Text chunks require valid keyword and compression fields, separators and UTF-8 where applicable. Extracting matching compressed text checks its checksum; malformed text raises `InvalidDataException` instead of silently yielding a partial payload. Well-formed text that is not Base64 remains ordinary metadata and is ignored during extraction.

JPEG metadata loading checks marker and segment boundaries through `EOI`, including later scans, while preserving scan bytes and marker fill bytes. Progressive JPEG remains supported. Both metadata readers reject trailing bytes after the end marker. These checks cover container structure; they do not decode PNG image data or JPEG entropy codes to validate pixels. Unrelated compressed text is preserved without decompression, and configurable decompression limits are not yet available.

Metadata payloads retain their existing format: entries are concatenated in file order, with no part identifiers or integrity checks. Use an authenticated `StegoPipeline` to detect payload damage. `MetadataCoding.MaxEntries` limits writing, not extraction; extraction checks store limits and the maximum byte-array length before assembling the payload. JPEG payload entries are read and replaced only before the first scan.

## Crypto configuration and registration

`SymmetricCrypto.Key` copies assigned key or passphrase bytes. Changing the caller's array afterward does not change the configured key; assigning `null` or an empty array clears it. Each encryption/decryption operation captures its settings before invoking the algorithm factory. Configure the object before use and do not change settings concurrently with an operation. Concurrent operations with unchanged settings are supported when the factory is thread-safe and returns a fresh algorithm for each call; each operation disposes its algorithm, including on failure.

The process-wide PRNG and cipher registries use case-sensitive names and preserve the first registration. Null or blank names are rejected. Existing type-based registration remains available: PRNG types need a public constructor taking exactly one `int`, and cipher types need a public parameterless constructor. Abstract and open generic types are rejected, except `typeof(Aes)`, which continues to use `Aes.Create()`. Unsupported types return `false` without reserving the name. Constructors are checked using .NET's [public constructor reflection API](https://learn.microsoft.com/en-us/dotnet/api/system.type.getconstructors).

Use `PRNG.RegisterPRNGFactory(name, seed => new CustomRandom(seed))` or `SymmetricCrypto.RegisterAlgorithmFactory(name, () => Aes.Create())` when construction needs a delegate. Registration does not invoke the factory. Factory exceptions propagate when the instance is requested; a null result raises `InvalidOperationException`. Factories may be invoked concurrently and must return separate instances. `PRNG.Name` selects the generator for the next `Initialize`; changing the name or failing to initialize preserves the previous generator and its position. Sharing a `PRNG` instance requires external synchronization.

These changes retain seeded `Random` sequences and the encrypted message format. Nullability analysis is now enabled for `PRNG` and `SymmetricCrypto`; broader API annotation work remains in progress.

## Payload codec contracts

`PayloadEnvelope` copies the codec list and retains the codec objects. Configure codecs before registration: their identifiers and fixed, nonnegative overhead must stay unchanged. The envelope rejects invalid overhead during construction and checks codec declarations and output lengths during use. A codec that returns null when sealing, or reports successful opening with null or incorrectly sized plaintext, raises `InvalidOperationException`. These checks prevent null codec outputs from being mistaken for valid empty payloads. Codec output arrays belong to the caller and must not reuse buffers retained by the codec.

Envelope and pipeline APIs now carry nullable annotations. `ExtractResult.Data` is nullable; checking `IsSuccess` lets nullable-aware callers safely access it. The returned byte array remains mutable, and copying an `ExtractResult` shares that array. `ExtractResult.Success(data)` retains the supplied array; passing null still means an empty payload for compatibility. Pipelines reject null keys before invoking carrier code.

The existing envelope format is unchanged. Unknown flags on an authenticated envelope produce `Unsupported`; changing flags without a valid authentication tag still produces `AuthenticationFailed`. Keep algorithm, envelope and codec configuration stable during operations. Concurrent use requires independent carriers and thread-safe dependencies. Configurable extraction limits and complete compressed-stream validation remain planned work.

## Metadata and carrier ownership

`IMetadataStore.WriteEntries` copies entry buffers, and `ReadEntries` returns independent copies. This contract now applies consistently to PNG, JPEG and WAV; editing an input buffer or an extracted entry does not change the store. Invalid entry lists leave existing entries intact. Custom stores must follow the same ownership and failure contracts.

Chunk and segment collections are read-only views; metadata-store views reflect later `WriteEntries` calls. Their low-level body arrays remain editable for compatibility, and callers must keep those edits valid for the container format. Use `WriteEntries` for payload replacement. Standalone `PngChunk`, `RiffChunk` and `JpegSegment` objects retain their constructor arrays without copying them. Metadata helpers and stores now have nullable annotations, and helpers reject null payloads or keys before reading input.

`PcmAudio.Clone()` and `JpegImage.Clone()` now copy metadata payloads as well as samples, coefficients and quantisation tables. Editing cloned metadata no longer changes the original. `PcmAudio` still shares the sample array passed to its constructor; use `Clone()` when an independent copy is needed. Synchronise access to mutable carriers and stores when sharing them between threads.

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

## Versioning and releases

I use [SemVer](http://semver.org/) for versioning. For the versions available, see the [tags on this repository](https://github.com/asmie/SteganoLib).

Pushing a tag of the form `vX.Y.Z` runs the release workflow, which builds and tests in Release, packs `SteganoLib` and `SteganoLib.Cli` with that version, pushes both to NuGet using the `NUGET_API_KEY` repository secret, and attaches the packages to a GitHub release with generated notes. The version in the project files is only a fallback for local packing.

## Authors

* **Piotr Olszewski** - *Original work* - [asmie](https://github.com/asmie)

See also the list of [contributors](https://github.com/asmie/SteganoLib/contributors) who participated in this project.


## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details
