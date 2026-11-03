namespace Zafiro.FigReader.Core.Kiwi;

/// <summary>
/// A decoded Kiwi <c>message</c> or <c>struct</c>: a bag of named field values plus its type name.
/// Field values are native CLR types (bool, byte, int, uint, float, long, ulong, string),
/// enum names (string), nested <see cref="KiwiObject"/>, <c>byte[]</c>, or <see cref="List{T}"/> of those.
/// </summary>
public sealed class KiwiObject
{
    private readonly Dictionary<string, object?> _fields;

    public KiwiObject(string typeName, Dictionary<string, object?> fields)
    {
        TypeName = typeName;
        _fields = fields;
    }

    public string TypeName { get; }

    public IReadOnlyDictionary<string, object?> Fields => _fields;

    public bool Has(string name) => _fields.ContainsKey(name);

    public object? Get(string name) => _fields.GetValueOrDefault(name);

    public KiwiObject? GetObject(string name) => Get(name) as KiwiObject;

    public string? GetString(string name) => Get(name) as string;

    public bool? GetBool(string name) => Get(name) as bool?;

    public IReadOnlyList<object?>? GetList(string name) => Get(name) as List<object?>;

    public double? GetNumber(string name) => Get(name) switch
    {
        null => null,
        float f => f,
        double d => d,
        int i => i,
        uint u => u,
        long l => l,
        ulong ul => ul,
        byte b => b,
        _ => null,
    };

    public override string ToString() => $"{TypeName} {{ {string.Join(", ", _fields.Keys)} }}";
}

/// <summary>
/// Decodes a Kiwi message buffer into <see cref="KiwiObject"/> trees using an embedded
/// <see cref="KiwiSchema"/>. Ported from the generated decoder logic in evanw/kiwi (js.ts).
/// </summary>
public sealed class KiwiMessageDecoder
{
    private readonly KiwiSchema _schema;

    public KiwiMessageDecoder(KiwiSchema schema)
    {
        _schema = schema;
    }

    public KiwiObject Decode(byte[] message, string rootType) => Decode(new ByteBuffer(message), rootType);

    public KiwiObject Decode(ByteBuffer bb, string rootType)
    {
        var value = DecodeDefinition(bb, _schema[rootType]);
        return (KiwiObject)value!;
    }

    private object? DecodeDefinition(ByteBuffer bb, KiwiDefinition definition)
    {
        switch (definition.Kind)
        {
            case KiwiKind.Enum:
                return definition.EnumName(bb.ReadVarUint());

            case KiwiKind.Message:
            {
                var fields = new Dictionary<string, object?>();
                while (true)
                {
                    var id = bb.ReadVarUint();
                    if (id == 0)
                    {
                        return new KiwiObject(definition.Name, fields);
                    }

                    var field = definition.FieldById(id)
                        ?? throw new InvalidDataException(
                            $"Kiwi: unknown field id {id} in message '{definition.Name}'.");
                    fields[field.Name] = ReadValue(bb, field);
                }
            }

            case KiwiKind.Struct:
            {
                var fields = new Dictionary<string, object?>(definition.Fields.Count);
                foreach (var field in definition.Fields)
                {
                    fields[field.Name] = ReadValue(bb, field);
                }

                return new KiwiObject(definition.Name, fields);
            }

            default:
                throw new InvalidDataException($"Kiwi: invalid definition kind {definition.Kind}.");
        }
    }

    private object? ReadValue(ByteBuffer bb, KiwiField field)
    {
        if (field.IsArray)
        {
            if (field.Type == "byte")
            {
                return bb.ReadByteArray();
            }

            var length = (int)bb.ReadVarUint();
            var list = new List<object?>(length);
            for (var i = 0; i < length; i++)
            {
                list.Add(ReadScalar(bb, field.Type!));
            }

            return list;
        }

        return ReadScalar(bb, field.Type!);
    }

    private object? ReadScalar(ByteBuffer bb, string type) => type switch
    {
        "bool" => bb.ReadByte() != 0,
        "byte" => bb.ReadByte(),
        "int" => bb.ReadVarInt(),
        "uint" => bb.ReadVarUint(),
        "float" => bb.ReadVarFloat(),
        "string" => bb.ReadString(),
        "int64" => bb.ReadVarInt64(),
        "uint64" => bb.ReadVarUint64(),
        _ => DecodeDefinition(bb, _schema[type]),
    };
}
