using System;
using System.Runtime.InteropServices;

namespace DotNetCheck;

internal static class ConsoleWindowHelpers
{
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public static void BringToFront()
    {
        var hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero)
            return;

        // make the window top-most
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

        // give it the keyboard focus
        SetForegroundWindow(hWnd);
    }

    // https://learn.microsoft.com/en-us/windows/console/getconsolewindow
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}