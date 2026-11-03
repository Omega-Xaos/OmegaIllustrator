using System.IO.Compression;
using System.Text;
using Zafiro.FigReader.Core.Kiwi;

namespace Zafiro.FigReader.Core;

/// <summary>
/// Reads a Figma <c>.fig</c> file. Modern files are a ZIP archive containing <c>canvas.fig</c>
/// (the design), <c>meta.json</c>, <c>thumbnail.png</c>, and <c>images/</c> + <c>videos/</c> blobs.
/// Older files are a bare <c>fig-kiwi</c> stream. Image/video blobs are read on demand to avoid
/// loading the whole archive (which can be hundreds of MB) into memory.
/// </summary>
public sealed class FigFile
{
    private static readonly byte[] FigKiwiMagic = "fig-kiwi"u8.ToArray();

    private byte[][]? _geometryBlobs;

    private FigFile(string path, CanvasFile canvas, string? metaJson, IReadOnlyList<string> blobEntries)
    {
        Path = path;
        Schema = canvas.Schema;
        Message = canvas.Message;
        MetaJson = metaJson;
        BlobEntries = blobEntries;
    }

    public string Path { get; }
    public KiwiSchema Schema { get; }

    /// <summary>The decoded root <c>Message</c> (its <c>nodeChanges</c> holds the flat node list).</summary>
    public KiwiObject Message { get; }

    /// <summary>Raw contents of <c>meta.json</c> if present (file name, thumbnail size, etc.).</summary>
    public string? MetaJson { get; }

    /// <summary>Names of embedded blob entries (under <c>images/</c> and <c>videos/</c>).</summary>
    public IReadOnlyList<string> BlobEntries { get; }

    /// <summary>True when the file is a ZIP archive (and therefore can contain blobs).</summary>
    public bool IsArchive => BlobEntries.Count > 0 || MetaJson is not null;

    public static FigFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Figma file not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        if (IsZip(stream))
        {
            return LoadFromZip(path, stream);
        }

        // Bare fig-kiwi file.
        stream.Position = 0;
        var bytes = ReadAll(stream);
        if (!bytes.AsSpan(0, Math.Min(8, bytes.Length)).SequenceEqual(FigKiwiMagic))
        {
            throw new InvalidDataException("Unrecognized .fig file: neither a ZIP archive nor a fig-kiwi stream.");
        }

        return new FigFile(path, CanvasDecoder.Decode(bytes), metaJson: null, Array.Empty<string>());
    }

    private static FigFile LoadFromZip(string path, Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var canvasEntry = archive.GetEntry("canvas.fig")
            ?? throw new InvalidDataException("ZIP .fig archive does not contain 'canvas.fig'.");
        var canvasBytes = ReadEntry(canvasEntry);
        var canvas = CanvasDecoder.Decode(canvasBytes);

        var metaEntry = archive.GetEntry("meta.json");
        var metaJson = metaEntry is null ? null : Encoding.UTF8.GetString(ReadEntry(metaEntry));

        var blobEntries = archive.Entries
            .Where(e => e.Length > 0 &&
                        (e.FullName.StartsWith("images/", StringComparison.Ordinal) ||
                         e.FullName.StartsWith("videos/", StringComparison.Ordinal)))
            .Select(e => e.FullName)
            .ToList();

        return new FigFile(path, canvas, metaJson, blobEntries);
    }

    /// <summary>Reads a blob/entry by its archive name (e.g. <c>images/&lt;hash&gt;</c>), or null if absent.</summary>
    public byte[]? ReadBlob(string entryName)
    {
        if (!IsArchive)
        {
            return null;
        }

        using var stream = File.OpenRead(Path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        return entry is null ? null : ReadEntry(entry);
    }

    /// <summary>
    /// Returns the bytes of the geometry blob at <paramref name="index"/> (from the message's
    /// <c>blobs</c> table, referenced by e.g. <c>fillGeometry[].commandsBlob</c>), or an empty
    /// array when the index is out of range.
    /// </summary>
    public byte[] GetGeometryBlob(int index)
    {
        _geometryBlobs ??= BuildGeometryBlobs();
        return index >= 0 && index < _geometryBlobs.Length ? _geometryBlobs[index] : Array.Empty<byte>();
    }

    /// <summary>Number of geometry blobs in the message's <c>blobs</c> table.</summary>
    public int GeometryBlobCount
    {
        get
        {
            _geometryBlobs ??= BuildGeometryBlobs();
            return _geometryBlobs.Length;
        }
    }

    private byte[][] BuildGeometryBlobs()
    {
        var list = Message.GetList("blobs");
        if (list is null)
        {
            return Array.Empty<byte[]>();
        }

        var result = new byte[list.Count][];
        for (var i = 0; i < list.Count; i++)
        {
            result[i] = (list[i] as KiwiObject)?.Get("bytes") as byte[] ?? Array.Empty<byte>();
        }

        return result;
    }

    private static bool IsZip(Stream stream)
    {
        Span<byte> header = stackalloc byte[2];
        var read = stream.Read(header);
        stream.Position = 0;
        return read == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var ms = new MemoryStream(entry.Length > 0 && entry.Length < int.MaxValue ? (int)entry.Length : 0);
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
