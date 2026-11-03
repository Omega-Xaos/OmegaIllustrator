using System.Text.Json;
using System.Text.Json.Nodes;
using Zafiro.FigReader.Core.Kiwi;

namespace Zafiro.FigReader.Core.Extraction;

/// <summary>
/// Converts decoded Kiwi values (<see cref="KiwiObject"/>, lists, primitives, byte arrays) into
/// <see cref="JsonNode"/> trees for serialization back to the agent. Large byte arrays are summarized
/// rather than inlined, and recursion depth/array length can be bounded to keep responses small.
/// </summary>
public sealed class KiwiJsonOptions
{
    public int MaxDepth { get; init; } = 64;
    public int MaxArrayLength { get; init; } = int.MaxValue;

    /// <summary>When true, byte arrays are emitted as <c>{ "$bytes": N }</c> instead of base64.</summary>
    public bool SummarizeByteArrays { get; init; } = true;
}

public static class KiwiJson
{
    public static JsonNode? ToJson(object? value, KiwiJsonOptions? options = null) =>
        Convert(value, options ?? new KiwiJsonOptions(), 0);

    private static JsonNode? Convert(object? value, KiwiJsonOptions options, int depth)
    {
        switch (value)
        {
            case null:
                return null;
            case KiwiObject obj:
            {
                if (depth >= options.MaxDepth)
                {
                    return JsonValue.Create($"<{obj.TypeName}>");
                }

                var json = new JsonObject();
                foreach (var (key, child) in obj.Fields)
                {
                    json[key] = Convert(child, options, depth + 1);
                }

                return json;
            }

            case byte[] bytes:
                return options.SummarizeByteArrays
                    ? new JsonObject { ["$bytes"] = bytes.Length }
                    : JsonValue.Create(System.Convert.ToBase64String(bytes));

            case System.Collections.IEnumerable enumerable and not string:
            {
                var array = new JsonArray();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= options.MaxArrayLength)
                    {
                        array.Add(JsonValue.Create($"<+{Remaining(enumerable, count)} more>"));
                        break;
                    }

                    array.Add(Convert(item, options, depth + 1));
                    count++;
                }

                return array;
            }

            case bool b: return JsonValue.Create(b);
            case string s: return JsonValue.Create(s);
            // System.Text.Json rejects non-finite numbers; emit them as strings so raw dumps
            // (which occasionally contain Infinity/NaN transforms) don't fail to serialize.
            case float f: return float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
            case double d: return double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
            case int i: return JsonValue.Create(i);
            case uint u: return JsonValue.Create(u);
            case long l: return JsonValue.Create(l);
            case ulong ul: return JsonValue.Create(ul);
            case byte by: return JsonValue.Create(by);
            default: return JsonValue.Create(value.ToString());
        }
    }

    private static int Remaining(System.Collections.IEnumerable enumerable, int shown)
    {
        var total = 0;
        foreach (var _ in enumerable)
        {
            total++;
        }

        return Math.Max(0, total - shown);
    }

    public static string Serialize(JsonNode? node) =>
        node?.ToJsonString(SerializerOptions) ?? "null";

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };
}
