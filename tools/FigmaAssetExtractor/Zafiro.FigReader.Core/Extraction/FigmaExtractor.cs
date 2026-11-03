using System.Text.Json.Nodes;
using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;
using Zafiro.FigReader.Core.Resolution;

namespace Zafiro.FigReader.Core.Extraction;

/// <summary>
/// Produces compact, token-efficient views of Figma nodes for an AI agent: simplified node trees,
/// per-node detail, bounds, colors, auto-layout and text style. Raw access remains available via
/// <see cref="KiwiJson"/> for callers that need everything.
/// </summary>
public static class FigmaExtractor
{
    /// <summary>Builds a simplified node, optionally recursing into children down to <paramref name="depth"/>.</summary>
    public static JsonObject Simplify(
        FigmaNode node,
        int depth,
        VariableResolver variables,
        bool includeChildren = true,
        InstanceResolver? resolver = null)
    {
        var json = new JsonObject
        {
            ["id"] = node.Id,
            ["name"] = node.Name,
            ["type"] = node.Type,
        };

        if (node.Raw.GetBool("visible") == false)
        {
            json["visible"] = false;
        }

        AddProperties(json, node.Type, node.Raw, variables);

        if (includeChildren)
        {
            // Instances have no direct children; expand the symbol they render (with overrides applied).
            if (resolver is not null && node.Type == "INSTANCE")
            {
                if (depth <= 0)
                {
                    // Report the child count cheaply, without expanding the whole subtree.
                    var count = resolver.ResolvedChildCount(node);
                    if (count > 0)
                    {
                        json["childCount"] = count;
                    }

                    return json;
                }

                var resolvedRoot = resolver.Resolve(node, maxDepth: depth);
                if (resolvedRoot is not null && resolvedRoot.Children.Count > 0)
                {
                    var resolvedChildren = new JsonArray();
                    foreach (var child in resolvedRoot.Children)
                    {
                        resolvedChildren.Add(SimplifyResolved(child, depth - 1, variables));
                    }

                    json["children"] = resolvedChildren;
                    return json;
                }
            }

            if (node.Children.Count > 0)
            {
                if (depth <= 0)
                {
                    json["childCount"] = node.Children.Count;
                }
                else
                {
                    var children = new JsonArray();
                    foreach (var child in node.Children)
                    {
                        children.Add(Simplify(child, depth - 1, variables, includeChildren, resolver));
                    }

                    json["children"] = children;
                }
            }
        }

        return json;
    }

    /// <summary>
    /// Renders a node of an instance's <em>resolved</em> subtree: its effective properties (bounds, fills,
    /// typography, ... after overrides), text and visibility and, for swapped instances, the component that
    /// was swapped in. Positional bounds are in the symbol's local space. <paramref name="depth"/> bounds
    /// recursion just like <see cref="Simplify"/>.
    /// </summary>
    public static JsonObject SimplifyResolved(ResolvedNode node, int depth, VariableResolver variables)
    {
        var json = new JsonObject
        {
            ["id"] = node.Source.Id,
            ["name"] = node.Name,
            ["type"] = node.Type,
        };

        if (!node.Visible)
        {
            json["visible"] = false;
        }

        AddProperties(json, node.Type, node.Properties, variables);

        if (node.Type == "TEXT" && node.Text is not null && json["text"] is JsonObject text)
        {
            text["characters"] = node.Text;
        }

        if (node.SwapComponentName is not null)
        {
            json["swappedTo"] = node.SwapComponentName;
            json["swappedToId"] = node.SwapComponentId;
        }

        if (node.UnresolvedSwapId is not null)
        {
            json["unresolvedSwap"] = node.UnresolvedSwapId;
        }

        if (node.Children.Count > 0)
        {
            if (depth <= 0)
            {
                json["childCount"] = node.Children.Count;
            }
            else
            {
                var children = new JsonArray();
                foreach (var child in node.Children)
                {
                    children.Add(SimplifyResolved(child, depth - 1, variables));
                }

                json["children"] = children;
            }
        }

        return json;
    }

    private static void AddProperties(JsonObject json, string? type, KiwiObject properties, VariableResolver variables)
    {
        AddBounds(json, properties);

        var opacity = properties.GetNumber("opacity");
        if (opacity is not null && opacity < 1)
        {
            json["opacity"] = Round(opacity.Value);
        }

        var cornerRadius = properties.GetNumber("cornerRadius");
        if (cornerRadius is > 0)
        {
            json["cornerRadius"] = Round(cornerRadius.Value);
        }

        var fills = SimplifyPaints(properties.GetList("fillPaints"), variables);
        if (fills is not null)
        {
            json["fills"] = fills;
        }

        var strokes = SimplifyPaints(properties.GetList("strokePaints"), variables);
        if (strokes is not null)
        {
            json["strokes"] = strokes;
            var sw = properties.GetNumber("strokeWeight");
            if (sw is not null)
            {
                json["strokeWeight"] = Round(sw.Value);
            }
        }

        var layout = SimplifyLayout(properties);
        if (layout is not null)
        {
            json["layout"] = layout;
        }

        var text = SimplifyText(type, properties, variables);
        if (text is not null)
        {
            json["text"] = text;
        }
    }

    public static void AddBounds(JsonObject json, KiwiObject properties)
    {
        var size = properties.GetObject("size");
        var transform = properties.GetObject("transform");
        if (size is not null)
        {
            json["width"] = Round(size.GetNumber("x") ?? 0);
            json["height"] = Round(size.GetNumber("y") ?? 0);
        }

        if (transform is not null)
        {
            json["x"] = Round(transform.GetNumber("m02") ?? 0);
            json["y"] = Round(transform.GetNumber("m12") ?? 0);
        }
    }

    /// <summary>
    /// Simplifies a paint list. Solid paints become hex colors, using their bound variable's value when they
    /// are bound to one. A binding that cannot be resolved yields
    /// <c>{ "color": &lt;stored literal&gt;, "unresolvedVariable": &lt;reference&gt; }</c> so the literal is not
    /// mistaken for the authoritative color. Other paints are reported by type.
    /// </summary>
    public static JsonArray? SimplifyPaints(IReadOnlyList<object?>? paints, VariableResolver variables)
    {
        if (paints is null || paints.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var item in paints)
        {
            if (item is not KiwiObject paint)
            {
                continue;
            }

            if (paint.GetBool("visible") == false)
            {
                continue;
            }

            var type = paint.GetString("type");
            if (type == "SOLID" && variables.PaintColor(paint) is { } color)
            {
                var hex = ColorToHex(color.Color, paint.GetNumber("opacity"));
                array.Add(color.UnresolvedReference is null
                    ? JsonValue.Create(hex)
                    : new JsonObject { ["color"] = hex, ["unresolvedVariable"] = color.UnresolvedReference });
            }
            else
            {
                array.Add(type ?? "PAINT");
            }
        }

        return array.Count > 0 ? array : null;
    }

    private static JsonObject? SimplifyLayout(KiwiObject properties)
    {
        var mode = properties.GetString("stackMode");
        if (mode is null or "NONE")
        {
            return null;
        }

        var layout = new JsonObject { ["mode"] = mode };

        var gap = properties.GetNumber("stackSpacing");
        if (gap is not null)
        {
            layout["gap"] = Round(gap.Value);
        }

        var top = properties.GetNumber("stackVerticalPadding") ?? 0;
        var right = properties.GetNumber("stackPaddingRight") ?? 0;
        var bottom = properties.GetNumber("stackPaddingBottom") ?? 0;
        var left = properties.GetNumber("stackHorizontalPadding") ?? 0;
        if (top != 0 || right != 0 || bottom != 0 || left != 0)
        {
            layout["padding"] = new JsonArray { Round(top), Round(right), Round(bottom), Round(left) };
        }

        AddString(layout, "primaryAlign", properties.GetString("stackPrimaryAlignItems"));
        AddString(layout, "counterAlign", properties.GetString("stackCounterAlignItems"));
        AddString(layout, "primarySizing", properties.GetString("stackPrimarySizing"));
        AddString(layout, "counterSizing", properties.GetString("stackCounterSizing"));

        return layout;
    }

    public static JsonObject? SimplifyText(FigmaNode node, VariableResolver variables) =>
        SimplifyText(node.Type, node.Raw, variables);

    private static JsonObject? SimplifyText(string? type, KiwiObject properties, VariableResolver variables)
    {
        if (type != "TEXT")
        {
            return null;
        }

        var text = new JsonObject
        {
            ["characters"] = properties.GetObject("textData")?.GetString("characters"),
        };

        var font = properties.GetObject("fontName");
        if (font is not null)
        {
            AddString(text, "fontFamily", font.GetString("family"));
            AddString(text, "fontStyle", font.GetString("style"));
        }

        var fontSize = properties.GetNumber("fontSize");
        if (fontSize is not null)
        {
            text["fontSize"] = Round(fontSize.Value);
        }

        var lineHeight = properties.GetObject("lineHeight");
        if (lineHeight is not null)
        {
            var value = lineHeight.GetNumber("value");
            var units = lineHeight.GetString("units");
            if (value is not null)
            {
                text["lineHeight"] = units switch
                {
                    "PIXELS" => $"{Round(value.Value)}px",
                    "PERCENT" => $"{Round(value.Value)}%",
                    _ => Round(value.Value),
                };
            }
        }

        var letterSpacing = properties.GetObject("letterSpacing")?.GetNumber("value");
        if (letterSpacing is not null && letterSpacing != 0)
        {
            text["letterSpacing"] = Round(letterSpacing.Value);
        }

        AddString(text, "align", properties.GetString("textAlignHorizontal"));

        var colors = SimplifyPaints(properties.GetList("fillPaints"), variables);
        if (colors is { Count: > 0 })
        {
            text["color"] = colors[0]!.DeepClone();
        }

        return text;
    }

    public static string ColorToHex(KiwiObject color, double? extraOpacity = null)
    {
        var r = Channel(color.GetNumber("r"));
        var g = Channel(color.GetNumber("g"));
        var b = Channel(color.GetNumber("b"));
        var a = (color.GetNumber("a") ?? 1) * (extraOpacity ?? 1);
        var alpha = Channel(a);

        return alpha == 255
            ? $"#{r:X2}{g:X2}{b:X2}"
            : $"#{r:X2}{g:X2}{b:X2}{alpha:X2}";
    }

    private static int Channel(double? v) => (int)Math.Round(Math.Clamp(v ?? 0, 0, 1) * 255);

    private static double Round(double v) => Math.Round(v, 2);

    private static void AddString(JsonObject json, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json[key] = value;
        }
    }
}
