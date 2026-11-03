using System.Text;

namespace Zafiro.FigReader.Core.Kiwi;

/// <summary>
/// Sequential little-endian reader for the Kiwi binary format used by Figma `.fig` files.
/// Ported from evanw/kiwi (bb.ts). Read-only: only the operations needed to decode are implemented.
/// </summary>
public sealed class ByteBuffer
{
    private readonly byte[] _data;
    private int _index;

    public ByteBuffer(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public int Index => _index;
    public int Length => _data.Length;
    public bool HasRemaining => _index < _data.Length;

    public byte ReadByte()
    {
        if (_index >= _data.Length)
        {
            throw new IndexOutOfRangeException("Kiwi: index out of bounds while reading byte.");
        }

        return _data[_index++];
    }

    public uint ReadVarUint()
    {
        uint value = 0;
        var shift = 0;
        byte b;
        do
        {
            b = ReadByte();
            value |= (uint)(b & 127) << shift;
            shift += 7;
        } while ((b & 128) != 0 && shift < 35);

        return value;
    }

    public int ReadVarInt()
    {
        var value = ReadVarUint();
        return (value & 1) != 0 ? ~(int)(value >> 1) : (int)(value >> 1);
    }

    public ulong ReadVarUint64()
    {
        ulong value = 0;
        var shift = 0;
        byte b;
        while (((b = ReadByte()) & 128) != 0 && shift < 56)
        {
            value |= (ulong)(b & 127) << shift;
            shift += 7;
        }

        value |= (ulong)b << shift;
        return value;
    }

    public long ReadVarInt64()
    {
        var value = ReadVarUint64();
        var sign = value & 1;
        value >>= 1;
        return sign != 0 ? ~(long)value : (long)value;
    }

    public float ReadVarFloat()
    {
        var first = _data[_index];
        if (first == 0)
        {
            _index++;
            return 0f;
        }

        if (_index + 4 > _data.Length)
        {
            throw new IndexOutOfRangeException("Kiwi: index out of bounds while reading float.");
        }

        var bits = (uint)(_data[_index] | (_data[_index + 1] << 8) | (_data[_index + 2] << 16) | (_data[_index + 3] << 24));
        _index += 4;
        bits = (bits << 23) | (bits >> 9);
        return BitConverter.UInt32BitsToSingle(bits);
    }

    /// <summary>Reads a UTF-8, null-terminated string.</summary>
    public string ReadString()
    {
        var start = _index;
        while (_data[_index] != 0)
        {
            _index++;
        }

        var result = Encoding.UTF8.GetString(_data, start, _index - start);
        _index++; // skip null terminator
        return result;
    }

    public byte[] ReadByteArray()
    {
        var length = (int)ReadVarUint();
        var start = _index;
        var end = start + length;
        if (end > _data.Length)
        {
            throw new IndexOutOfRangeException("Kiwi: read array out of bounds.");
        }

        _index = end;
        var result = new byte[length];
        Array.Copy(_data, start, result, 0, length);
        return result;
    }
}
