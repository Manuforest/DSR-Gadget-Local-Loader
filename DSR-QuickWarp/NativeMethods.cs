using System;
using System.Runtime.InteropServices;

namespace DSR_QuickWarp
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAllocEx(
            IntPtr process,
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            byte[] buffer,
            UIntPtr size,
            out UIntPtr numberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRemoteThread(
            IntPtr process,
            IntPtr threadAttributes,
            UIntPtr stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
