using System.Buffers.Binary;
using System.IO.Compression;
using Zafiro.FigReader.Core.Kiwi;
using ZstdSharp;

namespace Zafiro.FigReader.Core;

/// <summary>
/// Result of decoding a <c>canvas.fig</c> stream: the embedded schema and the decoded root message.
/// </summary>
public sealed record CanvasFile(KiwiSchema Schema, KiwiObject Message);

/// <summary>
/// Decodes the <c>canvas.fig</c> payload of a Figma file.
/// Layout: ASCII magic <c>"fig-kiwi"</c> + <c>uint32</c> version, then length-prefixed chunks
/// (<c>uint32</c> length + payload). Chunk 0 is the Kiwi binary schema; the remaining chunks,
/// decompressed and concatenated, form the Kiwi message whose root type is <c>Message</c>.
/// Each chunk is compressed with either raw DEFLATE or Zstandard (detected by magic).
/// </summary>
public static class CanvasDecoder
{
    private static readonly byte[] Magic = "fig-kiwi"u8.ToArray();
    private const string RootType = "Message";

    public static CanvasFile Decode(byte[] canvasBytes)
    {
        if (canvasBytes.Length < 12 || !canvasBytes.AsSpan(0, 8).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a fig-kiwi canvas: missing 'fig-kiwi' magic.");
        }

        var offset = 12; // 8 magic + 4 version
        var chunks = new List<byte[]>();
        while (offset + 4 <= canvasBytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(canvasBytes.AsSpan(offset, 4));
            offset += 4;
            if (length == 0 || offset + length > canvasBytes.Length)
            {
                break;
            }

            var compressed = canvasBytes.AsSpan(offset, length);
            chunks.Add(Decompress(compressed));
            offset += length;
        }

        if (chunks.Count < 2)
        {
            throw new InvalidDataException("fig-kiwi canvas does not contain both a schema and a message chunk.");
        }

        var schema = KiwiSchema.Decode(chunks[0]);

        var messageBytes = chunks.Count == 2
            ? chunks[1]
            : Concat(chunks.Skip(1));

        var message = new KiwiMessageDecoder(schema).Decode(messageBytes, RootType);
        return new CanvasFile(schema, message);
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        // Zstandard frame magic: 0x28 0xB5 0x2F 0xFD (little-endian).
        if (compressed.Length >= 4 &&
            compressed[0] == 0x28 && compressed[1] == 0xB5 && compressed[2] == 0x2F && compressed[3] == 0xFD)
        {
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(compressed).ToArray();
        }

        // Otherwise: raw DEFLATE (no zlib/gzip header).
        using var input = new MemoryStream(compressed.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Concat(IEnumerable<byte[]> parts)
    {
        using var ms = new MemoryStream();
        foreach (var part in parts)
        {
            ms.Write(part, 0, part.Length);
        }

        return ms.ToArray();
    }
}
