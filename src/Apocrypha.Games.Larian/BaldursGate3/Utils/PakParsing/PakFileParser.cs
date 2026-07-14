using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DynamicData.Kernel;
using K4os.Compression.LZ4;
using Apocrypha.Games.Larian.BaldursGate3.Utils.LsxXmlParsing;
using NexusMods.Paths;
using ZstdSharp;

namespace Apocrypha.Games.Larian.BaldursGate3.Utils.PakParsing;

/// <summary>
/// Class to parse and extract files and data from a bg3 .pak file.
/// Credits to @insomnious for reverse engineering the format and implementing the parser. 
/// </summary>
public static class PakFileParser
{
#region Public Methods

    /// <summary>
    /// Parses a bg3 `.pak` file stream and extracts the metadata from the packed `meta.lsx` file.
    /// </summary>
    /// <exception cref="InvalidDataException">In case of errors during parsing</exception>
    public static LspkPackageFormat.PakMetaData ParsePakMeta(Stream pakFileStream)
    {
        using var br = new BinaryReader(pakFileStream);
        var headerData = ParseHeaderInternal(br);
        var fileList = ParseFileListInternal(br, (int)headerData.FileListOffset, headerData);

        var fileEntryInfo = fileList.FirstOrOptional(f => f.Name.Contains("meta.lsx"));
        if (!fileEntryInfo.HasValue)
        {
            throw new InvalidDataException($"Unable to find `meta.lsx` file in pak archive");
        }

        var metaStream = GetFileEntryStream(br, fileEntryInfo.Value);
        var metaFile = MetaLsxParser.ParseMetaFile(metaStream);

        var seConfig = GetScriptExtenderConfigMetaData(fileList, br);

        return new LspkPackageFormat.PakMetaData
        {
            MetaFileData = metaFile,
            ScriptExtenderConfigMetadata = seConfig,
        };
    }

    private static Optional<LspkPackageFormat.ScriptExtenderConfigMetadata> GetScriptExtenderConfigMetaData(List<LspkPackageFormat.FileEntryInfoCommon> fileList, BinaryReader br)
    {
        var seConfig = fileList.FirstOrOptional(
            f => f.Name.EndsWith("ScriptExtender/Config.json")
        );

        if (seConfig.HasValue)
        {
            var configStream = GetFileEntryBytes(br, seConfig.Value);
            var jsonReader = new Utf8JsonReader(configStream);
            while (jsonReader.Read())
            {
                if (jsonReader.TokenType != JsonTokenType.PropertyName || jsonReader.GetString() != "RequiredVersion")
                    continue;

                jsonReader.Read();
                if (jsonReader.TokenType == JsonTokenType.Number && jsonReader.TryGetInt32(out var requiredVersion))
                {
                    return new LspkPackageFormat.ScriptExtenderConfigMetadata
                    {
                        RequiresScriptExtender = true,
                        SeRequiredVersion = requiredVersion,
                    };
                }
            }
        }

        return Optional<LspkPackageFormat.ScriptExtenderConfigMetadata>.None;
    }

#endregion

#region Private Methods

    private static LspkPackageFormat.HeaderCommon ParseHeaderInternal(BinaryReader br)
    {
        var magic = br.ReadBytes(4);
        var signature = Encoding.UTF8.GetString(magic);

        if (signature != LspkPackageFormat.HeaderCommon.SIGNATURE_STRING)
        {
            throw new InvalidDataException($"Not a valid BG3 PAK. Magic signature `{signature}` does not match expected signature `{LspkPackageFormat.HeaderCommon.SIGNATURE_STRING}`");
        }

        var version = br.ReadUInt32();

        switch (version)
        {
            case 15:
                return new LspkPackageFormat.LSPKHeader15()
                {
                    Version = version,
                    FileListOffset = br.ReadUInt64(),
                    FileListSize = br.ReadUInt32(),
                    Flags = br.ReadByte(),
                    Priority = br.ReadByte(),
                    Md5 = br.ReadBytes(16),
                }.ToCommonHeader();
            case 16:
            case 18:
                // same as 16 header
                return new LspkPackageFormat.LSPKHeader16Or18()
                {
                    Version = version,
                    FileListOffset = br.ReadUInt64(),
                    FileListSize = br.ReadUInt32(),
                    Flags = br.ReadByte(),
                    Priority = br.ReadByte(),
                    Md5 = br.ReadBytes(16),
                    NumParts = br.ReadUInt16(),
                }.ToCommonHeader();
            default:
                throw new InvalidDataException($"Unrecognized Pak version: v{version}");
        }
    }

    /// <summary>
    /// Upper bound for a single decompressed file-list / file-entry allocation driven by
    /// attacker-controlled header fields. A .pak is untrusted third-party input (same input class
    /// as the FOMOD traversal, CODE_REVIEW.md §7 #20): without bounds, a crafted header claiming a
    /// multi-GB size allocates it before any data is read — an OOM DoS. Real BG3 file lists are a
    /// few MB; real mod files far below this.
    /// </summary>
    private const long MaxDecompressedAllocation = 512L * 1024 * 1024;

    private static List<LspkPackageFormat.FileEntryInfoCommon> ParseFileListInternal(BinaryReader br, int offset, LspkPackageFormat.HeaderCommon header)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var numOfFiles = br.ReadInt32();

        var compressedSize = br.ReadInt32();

        // All three values come straight from the file. Bound them before allocating: the count
        // and compressed size must fit in what the archive actually contains, and the decompressed
        // list must stay under the allocation cap (checked in long math so the multiply can't
        // overflow into a small/negative array length).
        var remaining = br.BaseStream.Length - br.BaseStream.Position;
        if (numOfFiles < 0 || compressedSize < 0 || compressedSize > remaining)
            throw new InvalidDataException($"Invalid Pak file list: {numOfFiles} files / {compressedSize} compressed bytes with {remaining} bytes remaining");

        var decompressedListSize = (long)numOfFiles * LspkPackageFormat.GetFileEntrySize(header);
        if (decompressedListSize > MaxDecompressedAllocation)
            throw new InvalidDataException($"Invalid Pak file list: {numOfFiles} entries would decompress to {decompressedListSize} bytes (limit {MaxDecompressedAllocation})");

        var compressedBytes = br.ReadBytes(compressedSize);

        var decompressedBytes = new byte[decompressedListSize];

        // Assumption that we always have LZ4 for v15-18 (same as LSLib) but could be wrong
        var numDecodedBytes = LZ4Codec.Decode(
            compressedBytes,
            0,
            compressedBytes.Length,
            decompressedBytes,
            0,
            decompressedBytes.Length
        );

        if (numDecodedBytes != decompressedBytes.Length)
        {
            throw new InvalidDataException($"Decompression failed: decompressed size {decompressedBytes.Length} does not match expected size {numDecodedBytes}");
        }

        // new mem stream from decompress bytes
        using var ms = new MemoryStream(decompressedBytes);
        using var msr = new BinaryReader(ms);

        // built up list of file entries
        var entries = new List<LspkPackageFormat.FileEntryInfoCommon>(numOfFiles);

        msr.BaseStream.Seek(0, SeekOrigin.Begin);

        for (var i = 0; i < numOfFiles; i++)
        {
            var entry = ParseFileEntryInternal(msr, (int)header.Version);

            entries.Add(entry);
        }

        return entries;
    }

    private static LspkPackageFormat.FileEntryInfoCommon ParseFileEntryInternal(BinaryReader br, int version)
    {
        switch (version)
        {
            case 15:
            case 16: // same as 15
            {
                return new LspkPackageFormat.FileEntry15Or16
                {
                    Name = br.ReadBytes(256),
                    OffsetInFile = br.ReadUInt64(),
                    SizeOnDisk = br.ReadUInt64(),
                    UncompressedSize = br.ReadUInt64(),
                    ArchivePart = br.ReadUInt32(),
                    Flags = br.ReadUInt32(),
                    Crc = br.ReadUInt32(),
                    Unknown2 = br.ReadUInt32()
                }.ToCommonFileEntry();
            }
            case 18:
            {
                return new LspkPackageFormat.FileEntry18
                {
                    Name = br.ReadBytes(256),
                    OffsetInFile1 = br.ReadUInt32(),
                    OffsetInFile2 = br.ReadUInt16(),
                    ArchivePart = br.ReadByte(),
                    Flags = br.ReadByte(),
                    SizeOnDisk = br.ReadUInt32(),
                    UncompressedSize = br.ReadUInt32()
                }.ToCommonFileEntry();
            }
            default:
                throw new InvalidDataException($"Unrecognized Pak version: v{version}");
        }
    }


    /// <summary>
    /// Validates a file entry's header-declared offset/sizes against the actual archive before any
    /// allocation or read: the entry must lie within the archive, and its claimed decompressed size
    /// must stay under <see cref="MaxDecompressedAllocation"/> — all three values are untrusted.
    /// </summary>
    private static byte[] ReadEntryRawData(BinaryReader br, LspkPackageFormat.FileEntryInfoCommon fileMeta)
    {
        var streamLength = br.BaseStream.Length;
        var offset = (long)fileMeta.OffsetInFile;
        var sizeOnDisk = (long)fileMeta.SizeOnDisk;

        if (offset < 0 || sizeOnDisk < 0 || sizeOnDisk > int.MaxValue || offset > streamLength || sizeOnDisk > streamLength - offset)
            throw new InvalidDataException($"Invalid Pak entry {fileMeta.Name}: offset {fileMeta.OffsetInFile} + size {fileMeta.SizeOnDisk} exceeds archive length {streamLength}");

        if ((long)fileMeta.UncompressedSize > MaxDecompressedAllocation)
            throw new InvalidDataException($"Invalid Pak entry {fileMeta.Name}: claimed decompressed size {fileMeta.UncompressedSize} exceeds limit {MaxDecompressedAllocation}");

        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        return br.ReadBytes((int)sizeOnDisk);
    }

    private static byte[] GetFileEntryBytes(BinaryReader br, LspkPackageFormat.FileEntryInfoCommon fileMeta)
    {
        var rawData = ReadEntryRawData(br, fileMeta);

        return fileMeta.Flags.Method() switch
        {
            LspkPackageFormat.CompressionMethod.None => rawData,
            LspkPackageFormat.CompressionMethod.LZ4 => DecompressLz4(fileMeta, rawData),
            LspkPackageFormat.CompressionMethod.Zlib => DecompressZlib(fileMeta, rawData),
            LspkPackageFormat.CompressionMethod.Zstd => DecompressZstd(fileMeta, rawData),
            _ => throw new InvalidDataException($"Unsupported compression method {fileMeta.Flags.Method()} for file {fileMeta.Name}")
        };

        byte[] DecompressLz4(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];

            var decodedSize = LZ4Codec.Decode(bytes, 0, bytes.Length,
                decompressedBytes, 0, decompressedBytes.Length
            );
            if (decodedSize != decompressedBytes.Length)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: decompressed size {decodedSize} does not match expected size {fileEntryInfoCommon.UncompressedSize}"
                );
            }

            return decompressedBytes;
        }

        byte[] DecompressZlib(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var ds = new ZLibStream(ms, CompressionMode.Decompress);
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];
            try
            {
                // Stream.Read may legally return fewer bytes than requested mid-stream, which the
                // old single-call check misreported as corruption; ReadExactly loops until the
                // buffer is full or the stream truly ends.
                ds.ReadExactly(decompressedBytes);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: stream ended before expected size {fileEntryInfoCommon.UncompressedSize}", e
                );
            }

            return decompressedBytes;
        }

        byte[] DecompressZstd(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var ds = new DecompressionStream(ms);
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];
            try
            {
                // Stream.Read may legally return fewer bytes than requested mid-stream, which the
                // old single-call check misreported as corruption; ReadExactly loops until the
                // buffer is full or the stream truly ends.
                ds.ReadExactly(decompressedBytes);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: stream ended before expected size {fileEntryInfoCommon.UncompressedSize}", e
                );
            }

            return decompressedBytes;
        }
    }

    private static Stream GetFileEntryStream(BinaryReader br, LspkPackageFormat.FileEntryInfoCommon fileMeta)
    {
        var rawData = ReadEntryRawData(br, fileMeta);

        return fileMeta.Flags.Method() switch
        {
            LspkPackageFormat.CompressionMethod.None => new MemoryStream(rawData),
            LspkPackageFormat.CompressionMethod.LZ4 => DecompressLz4(fileMeta, rawData),
            LspkPackageFormat.CompressionMethod.Zlib => DecompressZlib(fileMeta, rawData),
            LspkPackageFormat.CompressionMethod.Zstd => DecompressZstd(fileMeta, rawData),
            _ => throw new InvalidDataException($"Unsupported compression method {fileMeta.Flags.Method()} for file {fileMeta.Name}")
        };

        Stream DecompressLz4(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];

            var decodedSize = LZ4Codec.Decode(bytes, 0, bytes.Length,
                decompressedBytes, 0, decompressedBytes.Length
            );
            if (decodedSize != decompressedBytes.Length)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: decompressed size {decodedSize} does not match expected size {fileEntryInfoCommon.UncompressedSize}"
                );
            }

            return new MemoryStream(decompressedBytes);
        }

        Stream DecompressZlib(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var ds = new ZLibStream(ms, CompressionMode.Decompress);
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];
            try
            {
                // Stream.Read may legally return fewer bytes than requested mid-stream, which the
                // old single-call check misreported as corruption; ReadExactly loops until the
                // buffer is full or the stream truly ends.
                ds.ReadExactly(decompressedBytes);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: stream ended before expected size {fileEntryInfoCommon.UncompressedSize}", e
                );
            }

            return new MemoryStream(decompressedBytes);
        }

        Stream DecompressZstd(LspkPackageFormat.FileEntryInfoCommon fileEntryInfoCommon, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var ds = new DecompressionStream(ms);
            var decompressedBytes = new byte[fileEntryInfoCommon.UncompressedSize];
            try
            {
                // Stream.Read may legally return fewer bytes than requested mid-stream, which the
                // old single-call check misreported as corruption; ReadExactly loops until the
                // buffer is full or the stream truly ends.
                ds.ReadExactly(decompressedBytes);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException(
                    $"Failed to extract {fileEntryInfoCommon.Name} from Pak archive: stream ended before expected size {fileEntryInfoCommon.UncompressedSize}", e
                );
            }

            return new MemoryStream(decompressedBytes);
        }
    }

#endregion
}
