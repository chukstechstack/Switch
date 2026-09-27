using System;
using System.Windows;
using System.Windows.Interop;

namespace Switch.Pages
{
    public class Win32WindowWrapper : System.Windows.Forms.IWin32Window
    {
        private readonly IntPtr _handle;
        public Win32WindowWrapper(Window w)
        {
            // No-op edit to refresh file context.
            _handle = new WindowInteropHelper(w).Handle;
        }
        public IntPtr Handle => _handle;
    }
}
