using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CotwLiveTracker.Memory;

internal static class NativeMethods
{
    [Flags]
    internal enum ProcessAccessRights : uint
    {
        VmRead = 0x0010,
        QueryInformation = 0x0400,
        QueryLimitedInformation = 0x1000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        ProcessAccessRights desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle processHandle,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);
}
