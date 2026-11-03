namespace Zafiro.FigReader.Core.Kiwi;

public enum KiwiKind
{
    Enum = 0,
    Struct = 1,
    Message = 2,
}

public sealed class KiwiField
{
    public required string Name { get; init; }

    /// <summary>
    /// Resolved type name: a native type ("bool","byte","int","uint","float","string","int64","uint64")
    /// or the name of another definition. Null for enum members (which only carry a value).
    /// </summary>
    public string? Type { get; set; }

    public bool IsArray { get; init; }

    /// <summary>Field id (for messages) or the numeric value (for enum members).</summary>
    public uint Value { get; init; }
}

public sealed class KiwiDefinition
{
    public required string Name { get; init; }
    public required KiwiKind Kind { get; init; }
    public required List<KiwiField> Fields { get; init; }

    private Dictionary<uint, KiwiField>? _fieldsById;
    private Dictionary<uint, string>? _enumNamesByValue;

    public KiwiField? FieldById(uint id)
    {
        _fieldsById ??= Fields.ToDictionary(f => f.Value);
        return _fieldsById.GetValueOrDefault(id);
    }

    public string EnumName(uint value)
    {
        _enumNamesByValue ??= Fields
            .GroupBy(f => f.Value)
            .ToDictionary(g => g.Key, g => g.First().Name);
        return _enumNamesByValue.TryGetValue(value, out var name) ? name : value.ToString();
    }
}

/// <summary>
/// A decoded Kiwi binary schema. Figma `.fig` files embed their own schema, making the message
/// self-describing. Ported from evanw/kiwi (binary.ts <c>decodeBinarySchema</c>).
/// </summary>
public sealed class KiwiSchema
{
    private static readonly string[] NativeTypes =
        ["bool", "byte", "int", "uint", "float", "string", "int64", "uint64"];

    private readonly Dictionary<string, KiwiDefinition> _byName;

    public IReadOnlyList<KiwiDefinition> Definitions { get; }

    private KiwiSchema(List<KiwiDefinition> definitions)
    {
        Definitions = definitions;
        _byName = definitions.ToDictionary(d => d.Name);
    }

    public KiwiDefinition this[string name] => _byName[name];

    public bool TryGet(string name, out KiwiDefinition definition) =>
        _byName.TryGetValue(name, out definition!);

    public static KiwiSchema Decode(byte[] schemaBytes) => Decode(new ByteBuffer(schemaBytes));

    public static KiwiSchema Decode(ByteBuffer bb)
    {
        var definitionCount = (int)bb.ReadVarUint();
        var definitions = new List<KiwiDefinition>(definitionCount);

        // Raw type indices are kept aside; they reference definitions by index, so resolution
        // happens in a second pass once every definition name is known.
        var rawTypes = new List<int?[]>(definitionCount);

        for (var i = 0; i < definitionCount; i++)
        {
            var name = bb.ReadString();
            var kind = (KiwiKind)bb.ReadByte();
            var fieldCount = (int)bb.ReadVarUint();
            var fields = new List<KiwiField>(fieldCount);
            var fieldTypes = new int?[fieldCount];

            for (var j = 0; j < fieldCount; j++)
            {
                var fieldName = bb.ReadString();
                var type = bb.ReadVarInt();
                var isArray = (bb.ReadByte() & 1) != 0;
                var value = bb.ReadVarUint();

                fieldTypes[j] = kind == KiwiKind.Enum ? null : type;
                fields.Add(new KiwiField { Name = fieldName, IsArray = isArray, Value = value });
            }

            rawTypes.Add(fieldTypes);
            definitions.Add(new KiwiDefinition { Name = name, Kind = kind, Fields = fields });
        }

        for (var i = 0; i < definitionCount; i++)
        {
            var fields = definitions[i].Fields;
            var types = rawTypes[i];
            for (var j = 0; j < fields.Count; j++)
            {
                var type = types[j];
                if (type is null)
                {
                    fields[j].Type = null;
                }
                else if (type < 0)
                {
                    var nativeIndex = ~type.Value;
                    if (nativeIndex >= NativeTypes.Length)
                    {
                        throw new InvalidDataException($"Kiwi: invalid native type index {type}.");
                    }

                    fields[j].Type = NativeTypes[nativeIndex];
                }
                else
                {
                    if (type >= definitionCount)
                    {
                        throw new InvalidDataException($"Kiwi: invalid type reference {type}.");
                    }

                    fields[j].Type = definitions[type.Value].Name;
                }
            }
        }

        return new KiwiSchema(definitions);
    }
}
