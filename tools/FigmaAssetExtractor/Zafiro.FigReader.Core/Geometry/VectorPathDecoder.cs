using System.Globalization;
using System.Text;
using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;

namespace Zafiro.FigReader.Core.Geometry;

/// <summary>
/// Decodes Figma vector geometry (the <c>commandsBlob</c> referenced by a node's
/// <c>fillGeometry</c>/<c>strokeGeometry</c>) into an SVG path string.
/// </summary>
/// <remarks>
/// A commands blob is a flat little-endian stream of drawing commands. Each command is one op byte
/// followed by 32-bit float coordinates:
/// <list type="bullet">
/// <item><c>0</c> = close path (no coords)</item>
/// <item><c>1</c> = move to (x, y)</item>
/// <item><c>2</c> = line to (x, y)</item>
/// <item><c>3</c> = quadratic curve to (cx, cy, x, y)</item>
/// <item><c>4</c> = cubic curve to (c1x, c1y, c2x, c2y, x, y)</item>
/// </list>
/// Coordinates are in the node's local space. The <c>windingRule</c> (<c>ODD</c>/<c>NONZERO</c>)
/// maps to Avalonia's <c>F0</c>/<c>F1</c> fill-rule prefix.
/// </remarks>
public static class VectorPathDecoder
{
    /// <summary>A single decoded geometry path: its SVG data, fill rule prefix and bounds.</summary>
    public readonly record struct DecodedPath(
        string Svg,
        string FillRule,
        double MinX,
        double MinY,
        double MaxX,
        double MaxY);

    /// <summary>
    /// Decodes every path in the given geometry list (e.g. <c>fillGeometry</c>). Returns an empty
    /// list when the node has no such geometry.
    /// </summary>
    public static IReadOnlyList<DecodedPath> Decode(FigFile file, FigmaNode node, string geometryField = "fillGeometry")
    {
        var geometry = node.Raw.GetList(geometryField);
        if (geometry is null || geometry.Count == 0)
        {
            return Array.Empty<DecodedPath>();
        }

        var result = new List<DecodedPath>(geometry.Count);
        foreach (var item in geometry)
        {
            if (item is not KiwiObject path)
            {
                continue;
            }

            var index = (int)(path.GetNumber("commandsBlob") ?? -1);
            if (index < 0)
            {
                continue;
            }

            var winding = path.GetString("windingRule");
            var bytes = file.GetGeometryBlob(index);
            if (bytes.Length == 0)
            {
                continue;
            }

            result.Add(DecodeBlob(bytes, winding));
        }

        return result;
    }

    /// <summary>Decodes a single commands blob into an SVG path.</summary>
    public static DecodedPath DecodeBlob(byte[] bytes, string? windingRule)
    {
        var sb = new StringBuilder();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var p = 0;

        float ReadFloat()
        {
            var v = BitConverter.ToSingle(bytes, p);
            p += 4;
            return v;
        }

        void Extend(float x, float y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        while (p < bytes.Length)
        {
            var op = bytes[p++];
            switch (op)
            {
                case 0:
                    sb.Append("Z ");
                    break;
                case 1 when p + 8 <= bytes.Length:
                {
                    float x = ReadFloat(), y = ReadFloat();
                    Extend(x, y);
                    sb.Append($"M{N(x)},{N(y)} ");
                    break;
                }
                case 2 when p + 8 <= bytes.Length:
                {
                    float x = ReadFloat(), y = ReadFloat();
                    Extend(x, y);
                    sb.Append($"L{N(x)},{N(y)} ");
                    break;
                }
                case 3 when p + 16 <= bytes.Length:
                {
                    float cx = ReadFloat(), cy = ReadFloat(), x = ReadFloat(), y = ReadFloat();
                    Extend(x, y);
                    sb.Append($"Q{N(cx)},{N(cy)} {N(x)},{N(y)} ");
                    break;
                }
                case 4 when p + 24 <= bytes.Length:
                {
                    float c1x = ReadFloat(), c1y = ReadFloat(), c2x = ReadFloat(), c2y = ReadFloat(), x = ReadFloat(), y = ReadFloat();
                    Extend(x, y);
                    sb.Append($"C{N(c1x)},{N(c1y)} {N(c2x)},{N(c2y)} {N(x)},{N(y)} ");
                    break;
                }
                default:
                    // Unknown or truncated op: stop rather than emit garbage.
                    p = bytes.Length;
                    break;
            }
        }

        var fillRule = string.Equals(windingRule, "ODD", StringComparison.OrdinalIgnoreCase) ? "F0" : "F1";
        if (double.IsInfinity(minX))
        {
            minX = minY = maxX = maxY = 0;
        }

        return new DecodedPath($"{fillRule} {sb.ToString().Trim()}".Trim(), fillRule, minX, minY, maxX, maxY);
    }

    private static string N(float v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
}
