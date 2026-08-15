using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DSR_QuickWarp
{
    internal static class OverlayInjector
    {
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint WAIT_OBJECT_0 = 0x00000000;

        internal static bool Inject(Process process, string dllPath, out string error)
        {
            error = null;
            if (process == null || process.HasExited)
            {
                error = "Game process is not running.";
                return false;
            }

            dllPath = Path.GetFullPath(dllPath);
            if (IsModuleLoaded(process, dllPath))
                return true;

            IntPtr processHandle = IntPtr.Zero;
            IntPtr remoteMemory = IntPtr.Zero;
            IntPtr threadHandle = IntPtr.Zero;

            try
            {
                processHandle = NativeMethods.OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                    false,
                    process.Id);
                if (processHandle == IntPtr.Zero)
                    throw new InvalidOperationException("OpenProcess failed (" + Marshal.GetLastWin32Error() + ").");

                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                remoteMemory = NativeMethods.VirtualAllocEx(
                    processHandle,
                    IntPtr.Zero,
                    (UIntPtr)pathBytes.Length,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE);
                if (remoteMemory == IntPtr.Zero)
                    throw new InvalidOperationException("VirtualAllocEx failed (" + Marshal.GetLastWin32Error() + ").");

                UIntPtr written;
                if (!NativeMethods.WriteProcessMemory(processHandle, remoteMemory, pathBytes, (UIntPtr)pathBytes.Length, out written)
                    || written.ToUInt64() != (ulong)pathBytes.Length)
                    throw new InvalidOperationException("WriteProcessMemory failed (" + Marshal.GetLastWin32Error() + ").");

                IntPtr localKernel32 = NativeMethods.GetModuleHandle("kernel32.dll");
                IntPtr localLoadLibrary = NativeMethods.GetProcAddress(localKernel32, "LoadLibraryW");
                if (localKernel32 == IntPtr.Zero || localLoadLibrary == IntPtr.Zero)
                    throw new InvalidOperationException("Could not resolve LoadLibraryW.");

                IntPtr remoteKernel32 = FindRemoteModule(process, "kernel32.dll");
                if (remoteKernel32 == IntPtr.Zero)
                    throw new InvalidOperationException("Could not locate kernel32.dll in the game process.");

                long loadLibraryOffset = localLoadLibrary.ToInt64() - localKernel32.ToInt64();
                IntPtr remoteLoadLibrary = new IntPtr(remoteKernel32.ToInt64() + loadLibraryOffset);

                threadHandle = NativeMethods.CreateRemoteThread(
                    processHandle,
                    IntPtr.Zero,
                    UIntPtr.Zero,
                    remoteLoadLibrary,
                    remoteMemory,
                    0,
                    IntPtr.Zero);
                if (threadHandle == IntPtr.Zero)
                    throw new InvalidOperationException("CreateRemoteThread failed (" + Marshal.GetLastWin32Error() + ").");

                uint wait = NativeMethods.WaitForSingleObject(threadHandle, 5000);
                if (wait != WAIT_OBJECT_0)
                    throw new InvalidOperationException("Overlay injection timed out.");

                uint exitCode;
                if (!NativeMethods.GetExitCodeThread(threadHandle, out exitCode) || exitCode == 0)
                    throw new InvalidOperationException("LoadLibraryW failed inside the game process.");

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (threadHandle != IntPtr.Zero)
                    NativeMethods.CloseHandle(threadHandle);
                if (remoteMemory != IntPtr.Zero && processHandle != IntPtr.Zero)
                    NativeMethods.VirtualFreeEx(processHandle, remoteMemory, UIntPtr.Zero, MEM_RELEASE);
                if (processHandle != IntPtr.Zero)
                    NativeMethods.CloseHandle(processHandle);
            }
        }

        private static bool IsModuleLoaded(Process process, string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(Path.GetFullPath(module.FileName), full, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static IntPtr FindRemoteModule(Process process, string moduleName)
        {
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                        return module.BaseAddress;
                }
            }
            catch
            {
            }
            return IntPtr.Zero;
        }
    }
}
