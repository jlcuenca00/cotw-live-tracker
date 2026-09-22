using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CotwLiveTracker.Memory;

internal sealed class ReadOnlyProcessMemory : IDisposable
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
