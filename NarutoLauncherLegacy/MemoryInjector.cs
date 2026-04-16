using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace NarutoLauncherLegacy
{
    public static class MemoryInjector
    {
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
        [DllImport("kernel32.dll")] private static extern int SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        private const int PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public static int EncontrarProcessoDoFlash(HashSet<int> pidsJaInjetados)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'CefSharp.BrowserSubprocess.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        string cmdLine = obj["CommandLine"]?.ToString();
                        if (cmdLine != null && cmdLine.Contains("--type=ppapi"))
                        {
                            int pid = Convert.ToInt32(obj["ProcessId"]);

                            lock (pidsJaInjetados)
                            {
                                if (!pidsJaInjetados.Contains(pid)) return pid;
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        public static bool InjetarDLL(int processId)
        {
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpeedEngine.dll");
            if (!File.Exists(dllPath)) return false;

            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
                if (hProcess == IntPtr.Zero) return false;

                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                uint size = (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char)));
                IntPtr allocMemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

                if (allocMemAddress == IntPtr.Zero) return false;

                bool wrote = WriteProcessMemory(hProcess, allocMemAddress, Encoding.Default.GetBytes(dllPath), size, out _);
                if (!wrote) return false;

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
                return hThread != IntPtr.Zero;
            }
            catch { return false; }
        }

        public static bool EnviarComandoVelocidade(int pid, string multiplicador)
        {
            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", "NarutoEnginePipe_" + pid, PipeDirection.Out))
                {
                    pipeClient.Connect(1000);
                    using (var writer = new StreamWriter(pipeClient)) { writer.AutoFlush = true; writer.Write(multiplicador); }
                }
                return true;
            }
            catch { return false; }
        }

        public static void OtimizarRAM(bool incluirSubprocessos)
        {
            try
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                    if (incluirSubprocessos)
                    {
                        foreach (Process p in Process.GetProcessesByName("CefSharp.BrowserSubprocess"))
                            try { SetProcessWorkingSetSize(p.Handle, -1, -1); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}