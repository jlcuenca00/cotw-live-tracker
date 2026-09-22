using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CotwLiveTracker.Memory;

internal interface IMemoryReader
{
    byte[] ReadBytes(nint address, int length);
}

internal sealed class ReadOnlyProcessMemory : IMemoryReader, IDisposable
{
    private readonly SafeProcessHandle _handle;

    private ReadOnlyProcessMemory(SafeProcessHandle handle, int processId)
    {
        _handle = handle;
        ProcessId = processId;
    }

    public int ProcessId { get; }

    public static ReadOnlyProcessMemory Open(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        const NativeMethods.ProcessAccessRights access =
            NativeMethods.ProcessAccessRights.VmRead |
            NativeMethods.ProcessAccessRights.QueryInformation |
            NativeMethods.ProcessAccessRights.QueryLimitedInformation;

        var handle = NativeMethods.OpenProcess(access, inheritHandle: false, (uint)process.Id);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open process {process.Id} with read-only access.");
        }

        return new ReadOnlyProcessMemory(handle, process.Id);
    }

    public byte[] ReadBytes(nint address, int length)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        if (address == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Address cannot be zero.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        var buffer = new byte[length];
        if (!NativeMethods.ReadProcessMemory(_handle, address, buffer, (nuint)length, out var bytesRead))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"ReadProcessMemory failed at 0x{address.ToInt64():X}.");
        }

        if (bytesRead != (nuint)length)
        {
            throw new InvalidOperationException(
                $"Partial memory read at 0x{address.ToInt64():X}: requested {length} bytes, received {bytesRead}.");
        }

        return buffer;
    }

    public void Dispose() => _handle.Dispose();
}

internal static class MemoryReaderExtensions
{
    public static nint ReadPointer(this IMemoryReader memory, nint address)
    {
        var bytes = memory.ReadBytes(address, sizeof(long));
        return (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    public static Float3 ReadFloat3(this IMemoryReader memory, nint address)
    {
        var bytes = memory.ReadBytes(address, 12);
        return new Float3(
            ReadSingle(bytes, 0),
            ReadSingle(bytes, 4),
            ReadSingle(bytes, 8));
    }

    public static string ReadNullTerminatedUtf8(this IMemoryReader memory, nint address, int maxLength)
    {
        var bytes = memory.ReadBytes(address, maxLength);
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    public static nint[] ReadPointers(this IMemoryReader memory, nint address, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return [];
        }

        var bytes = memory.ReadBytes(address, checked(count * sizeof(long)));
        var pointers = new nint[count];

        for (var i = 0; i < count; i++)
        {
            pointers[i] = (nint)BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(i * sizeof(long), sizeof(long)));
        }

        return pointers;
    }

    private static float ReadSingle(byte[] bytes, int offset)
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
        return BitConverter.Int32BitsToSingle(bits);
    }
}

internal readonly record struct Float3(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public float DistanceTo(Float3 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
