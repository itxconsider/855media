using System.Runtime.InteropServices;

namespace _855Media.Utils;

internal static class NativeMethods
{
    public static class Windows
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int MessageBox(nint hWnd, string text, string caption, uint type);
    }
}
