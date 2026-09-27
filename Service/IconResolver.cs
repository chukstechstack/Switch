using System;
using System.IO;

namespace Switch.Service
{
    // Lightweight placeholder: resolving .lnk reliably requires COM reference (IWshRuntimeLibrary).
    // To avoid adding a COM dependency in the project, this resolver currently returns null.
    // If you want full .lnk resolution, add a reference to Windows Script Host Object Model
    // (IWshRuntimeLibrary) and implement resolution in ResolveShortcut.
    public static class IconResolver
    {
        public static string? ResolveShortcut(string lnkPath)
        {
            try
            {
                if (!File.Exists(lnkPath)) return null;
                return null; // Not implemented
            }
            catch
            {
                return null;
            }
        }
    }
}
