using System.Buffers.Binary;
using System.Text;

namespace CotwLiveTracker.Population;

internal enum AdfMetaType : uint
{
    Primitive = 0,
    Structure = 1,
    Pointer = 2,
    Array = 3,
    InlineArray = 4,
    String = 5,
    Deferred = 6,
    Bitfield = 7,
    Enumeration = 8,
    StringHash = 9
}

internal sealed record AdfMemberDefinition(
    string Name,
    uint TypeHash,
    uint Size,
    uint Offset,
    byte BitOffset);

internal sealed record AdfTypeDefinition(
    AdfMetaType MetaType,
    uint Size,
    uint Alignment,
    uint TypeHash,
    string Name,
    uint Flags,
    uint ElementTypeHash,
    uint ElementLength,
    IReadOnlyList<AdfMemberDefinition> Members);

internal sealed record AdfInstanceDefinition(
    uint NameHash,
    uint TypeHash,
    uint Offset,
    uint Size,
    string Name);

internal sealed class AdfNode
{
    public AdfNode(uint typeHash, int dataOffset, object? value)
    {
        TypeHash = typeHash;
        DataOffset = dataOffset;
        Value = value;
    }

    public uint TypeHash { get; }
    public int DataOffset { get; }
    public object? Value { get; }

    public IReadOnlyDictionary<string, AdfNode> Fields =>
        Value as IReadOnlyDictionary<string, AdfNode>
        ?? throw new InvalidOperationException("ADF node is not a structure.");

    public IReadOnlyList<AdfNode> Items =>
        Value as IReadOnlyList<AdfNode>
        ?? throw new InvalidOperationException("ADF node is not an array.");

    public bool TryGetField(string name, out AdfNode? node)
    {
        node = null;
        return Value is IReadOnlyDictionary<string, AdfNode> fields &&
               fields.TryGetValue(name, out node);
    }

    public uint AsUInt32() => Value switch
    {
        byte value => value,
        ushort value => value,
        uint value => value,
        int value when value >= 0 => checked((uint)value),
        _ => throw new InvalidOperationException($"ADF value at 0x{DataOffset:X} is not an unsigned integer.")
    };

    public float AsSingle() => Value switch
    {
        float value => value,
        double value => checked((float)value),
        _ => throw new InvalidOperationException($"ADF value at 0x{DataOffset:X} is not a floating-point value.")
    };
}

internal sealed record AdfDocument(
    uint Version,
    IReadOnlyDictionary<uint, AdfTypeDefinition> Types,
    IReadOnlyList<AdfInstanceDefinition> Instances,
    IReadOnlyList<AdfNode> RootValues);

internal static class ApexAdfReader
{
    private const uint TypeS8 = 0x580D0A62;
    private const uint TypeU8 = 0x0CA2821D;
    private const uint TypeS16 = 0xD13FCF93;
    private const uint TypeU16 = 0x86D152BD;
    private const uint TypeS32 = 0x192FE633;
    private const uint TypeU32 = 0x075E4E4F;
    private const uint TypeS64 = 0xAF41354F;
    private const uint TypeU64 = 0xA139E01F;
    private const uint TypeF32 = 0x7515A207;
    private const uint TypeF64 = 0xC609F663;
    private const uint TypeString = 0x8955583E;
    private const uint TypeDeferred = 0xDEFE88ED;

    public static AdfDocument Read(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < 0x40)
        {
            throw new InvalidDataException("ADF payload is shorter than its 0x40-byte header.");
        }

        if (payload[0] != (byte)' ' ||
            payload[1] != (byte)'F' ||
            payload[2] != (byte)'D' ||
            payload[3] != (byte)'A')
        {
            throw new InvalidDataException("ADF magic does not match ' FDA'.");
        }

        var version = ReadUInt32(payload, 0x04);
        var instanceCount = ReadUInt32(payload, 0x08);
        var instanceOffset = ReadUInt32(payload, 0x0C);
        var typeCount = ReadUInt32(payload, 0x10);
        var typeOffset = ReadUInt32(payload, 0x14);
        var stringHashCount = ReadUInt32(payload, 0x18);
        var stringHashOffset = ReadUInt32(payload, 0x1C);
        var nameCount = ReadUInt32(payload, 0x20);
        var nameOffset = ReadUInt32(payload, 0x24);
        var totalSize = ReadUInt32(payload, 0x28);

        ValidateCount(instanceCount, "instance");
        ValidateCount(typeCount, "type");
        ValidateCount(stringHashCount, "string-hash");
        ValidateCount(nameCount, "name");

        if (totalSize > payload.Length)
        {
            throw new InvalidDataException(
                $"ADF header total size {totalSize:N0} exceeds payload length {payload.Length:N0}.");
        }

        var names = ReadNameTable(payload, checked((int)nameOffset), checked((int)nameCount));
        _ = ReadStringHashTableEnd(payload, checked((int)stringHashOffset), checked((int)stringHashCount));

        var types = ReadTypeDefinitions(payload, checked((int)typeOffset), checked((int)typeCount), names);
        var instances = ReadInstanceDefinitions(payload, checked((int)instanceOffset), checked((int)instanceCount), names);

        var roots = new List<AdfNode>(instances.Count);
        foreach (var instance in instances)
        {
            var offset = checked((int)instance.Offset);
            var size = checked((int)instance.Size);
            EnsureRange(payload, offset, size, "ADF instance");

            var instanceBuffer = payload.AsSpan(offset, size).ToArray();
            var node = ReadNode(
                instanceBuffer,
                0,
                instance.TypeHash,
                types,
                bitOffset: null,
                recursionDepth: 0);
            roots.Add(node);
        }

        return new AdfDocument(version, types, instances, roots);
    }

    private static IReadOnlyList<string> ReadNameTable(byte[] payload, int offset, int count)
    {
        EnsureRange(payload, offset, count, "ADF name length table");
        var lengths = payload.AsSpan(offset, count).ToArray();

        var names = new List<string>(count);
        var cursor = checked(offset + count);

        foreach (var length in lengths)
        {
            EnsureRange(payload, cursor, length + 1, "ADF name");
            var bytes = payload.AsSpan(cursor, length);
            names.Add(Encoding.UTF8.GetString(bytes));
            cursor = checked(cursor + length + 1);
        }

        return names;
    }

    private static int ReadStringHashTableEnd(byte[] payload, int offset, int count)
    {
        var cursor = offset;
        for (var i = 0; i < count; i++)
        {
            EnsureRange(payload, cursor, 1, "ADF string hash");
            while (true)
            {
                EnsureRange(payload, cursor, 1, "ADF string hash text");
                if (payload[cursor++] == 0)
                {
                    break;
                }
            }

            EnsureRange(payload, cursor, sizeof(ulong), "ADF string hash value");
            cursor += sizeof(ulong);
        }

        return cursor;
    }

    private static IReadOnlyDictionary<uint, AdfTypeDefinition> ReadTypeDefinitions(
        byte[] payload,
        int offset,
        int count,
        IReadOnlyList<string> names)
    {
        var cursor = offset;
        var types = new Dictionary<uint, AdfTypeDefinition>();

        for (var i = 0; i < count; i++)
        {
            EnsureRange(payload, cursor, 36, "ADF typedef header");
            var metaType = (AdfMetaType)ReadUInt32(payload, cursor);
            var size = ReadUInt32(payload, cursor + 4);
            var alignment = ReadUInt32(payload, cursor + 8);
            var typeHash = ReadUInt32(payload, cursor + 12);
            var nameIndex = ReadUInt64(payload, cursor + 16);
            var flags = ReadUInt32(payload, cursor + 24);
            var elementTypeHash = ReadUInt32(payload, cursor + 28);
            var elementLength = ReadUInt32(payload, cursor + 32);
            cursor += 36;

            var name = ResolveName(names, nameIndex);
            var members = new List<AdfMemberDefinition>();

            switch (metaType)
            {
                case AdfMetaType.Structure:
                {
                    var memberCount = ReadUInt32Checked(payload, ref cursor, "ADF structure member count");
                    ValidateCount(memberCount, "structure member");

                    for (var memberIndex = 0; memberIndex < memberCount; memberIndex++)
                    {
                        EnsureRange(payload, cursor, 32, "ADF structure member");
                        var memberNameIndex = ReadUInt64(payload, cursor);
                        var memberTypeHash = ReadUInt32(payload, cursor + 8);
                        var memberSize = ReadUInt32(payload, cursor + 12);
                        var packedOffset = ReadUInt32(payload, cursor + 16);
                        var memberOffset = packedOffset & 0x00FFFFFF;
                        var bitOffset = (byte)((packedOffset >> 24) & 0xFF);
                        members.Add(new AdfMemberDefinition(
                            ResolveName(names, memberNameIndex),
                            memberTypeHash,
                            memberSize,
                            memberOffset,
                            bitOffset));
                        cursor += 32;
                    }

                    break;
                }

                case AdfMetaType.Pointer:
                case AdfMetaType.Array:
                case AdfMetaType.InlineArray:
                case AdfMetaType.Bitfield:
                case AdfMetaType.StringHash:
                {
                    _ = ReadUInt32Checked(payload, ref cursor, "ADF typedef trailing count");
                    break;
                }

                case AdfMetaType.Enumeration:
                {
                    var enumCount = ReadUInt32Checked(payload, ref cursor, "ADF enum count");
                    ValidateCount(enumCount, "enum");
                    EnsureRange(payload, cursor, checked((int)enumCount * 12), "ADF enum values");
                    cursor += checked((int)enumCount * 12);
                    break;
                }

                case AdfMetaType.Primitive:
                case AdfMetaType.String:
                case AdfMetaType.Deferred:
                    break;

                default:
                    throw new InvalidDataException($"Unsupported ADF metatype {(uint)metaType}.");
            }

            types[typeHash] = new AdfTypeDefinition(
                metaType,
                size,
                alignment,
                typeHash,
                name,
                flags,
                elementTypeHash,
                elementLength,
                members);
        }

        return types;
    }

    private static IReadOnlyList<AdfInstanceDefinition> ReadInstanceDefinitions(
        byte[] payload,
        int offset,
        int count,
        IReadOnlyList<string> names)
    {
        var cursor = offset;
        var instances = new List<AdfInstanceDefinition>(count);

        for (var i = 0; i < count; i++)
        {
            EnsureRange(payload, cursor, 24, "ADF instance definition");
            var nameHash = ReadUInt32(payload, cursor);
            var typeHash = ReadUInt32(payload, cursor + 4);
            var dataOffset = ReadUInt32(payload, cursor + 8);
            var size = ReadUInt32(payload, cursor + 12);
            var nameIndex = ReadUInt64(payload, cursor + 16);
            instances.Add(new AdfInstanceDefinition(
                nameHash,
                typeHash,
                dataOffset,
                size,
                ResolveName(names, nameIndex)));
            cursor += 24;
        }

        return instances;
    }

    private static AdfNode ReadNode(
        byte[] buffer,
        int position,
        uint typeHash,
        IReadOnlyDictionary<uint, AdfTypeDefinition> types,
        byte? bitOffset,
        int recursionDepth)
    {
        if (recursionDepth > 64)
        {
            throw new InvalidDataException("ADF recursion depth exceeded the safe limit.");
        }

        if (TryReadPrimitive(buffer, position, typeHash, out var primitive))
        {
            return primitive!;
        }

        if (typeHash == TypeString)
        {
            EnsureRange(buffer, position, 8, "ADF string header");
            var stringOffset = checked((int)ReadUInt32(buffer, position));
            var length = checked((int)ReadUInt32(buffer, position + 4));
            EnsureRange(buffer, stringOffset, Math.Max(1, length), "ADF string data");

            var end = stringOffset;
            var limit = Math.Min(buffer.Length, checked(stringOffset + Math.Max(1, length)));
            while (end < limit && buffer[end] != 0)
            {
                end++;
            }

            return new AdfNode(typeHash, position, Encoding.UTF8.GetString(buffer, stringOffset, end - stringOffset));
        }

        if (typeHash == TypeDeferred)
        {
            EnsureRange(buffer, position, 16, "ADF deferred value");
            var valueOffset = checked((int)ReadUInt32(buffer, position));
            var deferredType = ReadUInt32(buffer, position + 8);
            if (valueOffset == 0 || deferredType == 0)
            {
                return new AdfNode(typeHash, position, null);
            }

            return new AdfNode(
                typeHash,
                position,
                ReadNode(buffer, valueOffset, deferredType, types, null, recursionDepth + 1));
        }

        if (!types.TryGetValue(typeHash, out var type))
        {
            throw new InvalidDataException($"ADF type 0x{typeHash:X8} is missing from the type table.");
        }

        switch (type.MetaType)
        {
            case AdfMetaType.Structure:
            {
                var fields = new Dictionary<string, AdfNode>(StringComparer.Ordinal);
                foreach (var member in type.Members)
                {
                    var memberPosition = checked(position + (int)member.Offset);
                    fields[member.Name] = ReadNode(
                        buffer,
                        memberPosition,
                        member.TypeHash,
                        types,
                        member.BitOffset,
                        recursionDepth + 1);
                }

                return new AdfNode(typeHash, position, fields);
            }

            case AdfMetaType.Pointer:
                EnsureRange(buffer, position, sizeof(ulong), "ADF pointer");
                return new AdfNode(typeHash, position, ReadUInt64(buffer, position));

            case AdfMetaType.Array:
            {
                EnsureRange(buffer, position, 12, "ADF array header");
                var dataOffset = checked((int)ReadUInt32(buffer, position));
                var length = checked((int)ReadUInt32(buffer, position + 8));
                ValidateArrayLength(length);
                return new AdfNode(
                    typeHash,
                    dataOffset,
                    ReadArrayItems(buffer, dataOffset, length, type.ElementTypeHash, types, recursionDepth + 1));
            }

            case AdfMetaType.InlineArray:
            {
                var length = checked((int)type.ElementLength);
                ValidateArrayLength(length);
                return new AdfNode(
                    typeHash,
                    position,
                    ReadArrayItems(buffer, position, length, type.ElementTypeHash, types, recursionDepth + 1));
            }

            case AdfMetaType.Bitfield:
            {
                var raw = type.Size switch
                {
                    1 => buffer[position],
                    2 => ReadUInt16(buffer, position),
                    4 => ReadUInt32(buffer, position),
                    8 => ReadUInt64(buffer, position),
                    _ => throw new InvalidDataException($"Unsupported bitfield size {type.Size}.")
                };
                var shift = bitOffset ?? 0;
                return new AdfNode(typeHash, position, (uint)((raw >> shift) & 1UL));
            }

            case AdfMetaType.Enumeration:
                EnsureRange(buffer, position, sizeof(uint), "ADF enum");
                return new AdfNode(typeHash, position, ReadUInt32(buffer, position));

            case AdfMetaType.StringHash:
            {
                object value = type.Size switch
                {
                    4 => ReadUInt32(buffer, position),
                    8 => ReadUInt64(buffer, position),
                    _ => buffer.AsSpan(position, checked((int)type.Size)).ToArray()
                };
                return new AdfNode(typeHash, position, value);
            }

            case AdfMetaType.Primitive:
                throw new InvalidDataException($"Unknown primitive ADF type 0x{typeHash:X8}.");

            default:
                throw new InvalidDataException(
                    $"ADF metatype {type.MetaType} is not required by the population reader.");
        }
    }

    private static IReadOnlyList<AdfNode> ReadArrayItems(
        byte[] buffer,
        int dataOffset,
        int length,
        uint elementTypeHash,
        IReadOnlyDictionary<uint, AdfTypeDefinition> types,
        int recursionDepth)
    {
        if (length == 0)
        {
            return [];
        }

        EnsureRange(buffer, dataOffset, 1, "ADF array data");
        var items = new List<AdfNode>(length);
        var cursor = dataOffset;

        for (var i = 0; i < length; i++)
        {
            var item = ReadNode(buffer, cursor, elementTypeHash, types, null, recursionDepth + 1);
            items.Add(item);
            cursor = checked(cursor + GetTypeSize(elementTypeHash, types));
            if (cursor > buffer.Length && i + 1 < length)
            {
                throw new InvalidDataException("ADF array extends beyond the instance buffer.");
            }
        }

        return items;
    }

    private static int GetTypeSize(uint typeHash, IReadOnlyDictionary<uint, AdfTypeDefinition> types) =>
        typeHash switch
        {
            TypeS8 or TypeU8 => 1,
            TypeS16 or TypeU16 => 2,
            TypeS32 or TypeU32 or TypeF32 => 4,
            TypeS64 or TypeU64 or TypeF64 => 8,
            TypeString => 8,
            TypeDeferred => 16,
            _ when types.TryGetValue(typeHash, out var type) => checked((int)type.Size),
            _ => throw new InvalidDataException($"Cannot determine size of ADF type 0x{typeHash:X8}.")
        };

    private static bool TryReadPrimitive(byte[] buffer, int position, uint typeHash, out AdfNode? node)
    {
        node = null;
        object value;

        switch (typeHash)
        {
            case TypeS8:
                EnsureRange(buffer, position, 1, "ADF s8");
                value = unchecked((sbyte)buffer[position]);
                break;
            case TypeU8:
                EnsureRange(buffer, position, 1, "ADF u8");
                value = buffer[position];
                break;
            case TypeS16:
                value = ReadInt16(buffer, position);
                break;
            case TypeU16:
                value = ReadUInt16(buffer, position);
                break;
            case TypeS32:
                value = ReadInt32(buffer, position);
                break;
            case TypeU32:
                value = ReadUInt32(buffer, position);
                break;
            case TypeS64:
                value = ReadInt64(buffer, position);
                break;
            case TypeU64:
                value = ReadUInt64(buffer, position);
                break;
            case TypeF32:
                value = ReadSingle(buffer, position);
                break;
            case TypeF64:
                value = ReadDouble(buffer, position);
                break;
            default:
                return false;
        }

        node = new AdfNode(typeHash, position, value);
        return true;
    }

    private static string ResolveName(IReadOnlyList<string> names, ulong index)
    {
        if (index >= (ulong)names.Count)
        {
            throw new InvalidDataException($"ADF name-table index {index} is outside the table.");
        }

        return names[checked((int)index)];
    }

    private static void ValidateCount(uint count, string label)
    {
        if (count > 1_000_000)
        {
            throw new InvalidDataException($"ADF {label} count {count:N0} exceeds the safe limit.");
        }
    }

    private static void ValidateArrayLength(int length)
    {
        if (length < 0 || length > 1_000_000)
        {
            throw new InvalidDataException($"ADF array length {length:N0} exceeds the safe limit.");
        }
    }

    private static uint ReadUInt32Checked(byte[] buffer, ref int position, string label)
    {
        EnsureRange(buffer, position, sizeof(uint), label);
        var value = ReadUInt32(buffer, position);
        position += sizeof(uint);
        return value;
    }

    private static void EnsureRange(byte[] buffer, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > buffer.Length - length)
        {
            throw new InvalidDataException(
                $"{label} range 0x{offset:X}+{length:N0} falls outside a {buffer.Length:N0}-byte buffer.");
        }
    }

    private static short ReadInt16(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(short), "ADF int16");
        return BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, sizeof(short)));
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(ushort), "ADF uint16");
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, sizeof(ushort)));
    }

    private static int ReadInt32(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(int), "ADF int32");
        return BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(uint), "ADF uint32");
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)));
    }

    private static long ReadInt64(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(long), "ADF int64");
        return BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset, sizeof(long)));
    }

    private static ulong ReadUInt64(byte[] buffer, int offset)
    {
        EnsureRange(buffer, offset, sizeof(ulong), "ADF uint64");
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, sizeof(ulong)));
    }

    private static float ReadSingle(byte[] buffer, int offset)
    {
        var bits = ReadInt32(buffer, offset);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static double ReadDouble(byte[] buffer, int offset)
    {
        var bits = ReadInt64(buffer, offset);
        return BitConverter.Int64BitsToDouble(bits);
    }
}
