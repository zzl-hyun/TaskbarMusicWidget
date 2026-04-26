using System;
using System.Runtime.InteropServices;

namespace TaskbarMusicWidget.Services;

public sealed class TaskbarAnchorService
{
    public RectD? GetTrayAnchorRect()
    {
        var shellTray = FindWindow("Shell_TrayWnd", null);
        if (shellTray == IntPtr.Zero)
        {
            return null;
        }

        var trayNotify = FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
        var target = trayNotify != IntPtr.Zero ? trayNotify : shellTray;

        if (!GetWindowRect(target, out var rect))
        {
            return null;
        }

        return new RectD(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public RectD? GetTaskbarRect()
    {
        var shellTray = FindWindow("Shell_TrayWnd", null);
        if (shellTray == IntPtr.Zero)
        {
            return null;
        }

        if (!GetWindowRect(shellTray, out var rect))
        {
            return null;
        }

        return new RectD(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public bool IsOverflowFlyoutOpen()
{
    var overflow1 = FindWindow("NotifyIconOverflowWindow", null);
    if (overflow1 != IntPtr.Zero && IsWindowVisible(overflow1))
    {
        return true;
    }

    var overflow2 = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
    return overflow2 != IntPtr.Zero && IsWindowVisible(overflow2);
}

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public readonly record struct RectD(double Left, double Top, double Width, double Height);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}