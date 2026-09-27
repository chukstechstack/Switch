using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Switch.Service
{
    public static class ProcessHelper
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        public static string? GetProcessExecutablePath(Process p)
        {
            try
            {
                var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (handle == IntPtr.Zero) return null;
                try
                {
                    var sb = new StringBuilder(1024);
                    uint capacity = (uint)sb.Capacity;
                    if (QueryFullProcessImageName(handle, 0, sb, ref capacity))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        public static string GetProcessExeName(Process p)
        {
            var path = GetProcessExecutablePath(p);
            if (!string.IsNullOrEmpty(path))
            {
                try { return System.IO.Path.GetFileNameWithoutExtension(path); } catch { }
            }
            try { return p.ProcessName; } catch { return string.Empty; }
        }

        // tighter match: return full exe path when available
        public static string? TryGetProcessExePath(Process p)
        {
            return GetProcessExecutablePath(p);
        }
    }
}
