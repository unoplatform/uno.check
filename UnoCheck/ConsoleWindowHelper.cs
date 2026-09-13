using System;
using System.Runtime.InteropServices;

namespace DotNetCheck;

internal static class ConsoleWindowHelpers
{
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST  = new(-2);
    
    public static void BringToFront()
    {
        var hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero)
            return;
        // ensure the console is shown (handles minimised state).
        ShowWindow(hWnd, ShowWindowCommands.Restore);
        
        // make the window top-most
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

        // then immediately cancel TopMost so it behaves normally
        SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        
        // give it the keyboard focus
        SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// Hides the console window entirely. Used in structured-output mode (--json/--json-file)
    /// where a host application owns the UX and the console must never appear — in particular
    /// for elevated fix children, whose window-style hint is dropped across the UAC boundary.
    /// Callers must first verify ownership via <see cref="OwnsConsole"/>: a process launched
    /// from an existing terminal shares that window, and hiding it would take out the user's
    /// own shell with nothing to bring it back.
    /// </summary>
    public static void Hide()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero)
            return;

        ShowWindow(hWnd, ShowWindowCommands.Hide);
    }

    /// <summary>
    /// True when this process is the only one attached to its console — i.e. the console
    /// was created for us (typical for a child spawned by a host app) rather than shared
    /// with the terminal the user typed into.
    /// </summary>
    public static bool OwnsConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        if (GetConsoleWindow() == IntPtr.Zero)
            return false;

        var processIds = new uint[2];
        return GetConsoleProcessList(processIds, (uint)processIds.Length) == 1;
    }

    // https://learn.microsoft.com/en-us/windows/console/getconsoleprocesslist
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    // https://learn.microsoft.com/en-us/windows/console/getconsolewindow
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

    private enum ShowWindowCommands
    {
        Hide      = 0,
        Normal    = 1,
        Restore   = 9
    }
}