using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarMusicWidget.Services;
using WinForms = System.Windows.Forms;

namespace TaskbarMusicWidget;

public partial class MainWindow : Window
{
    private readonly bool _hardFixedMode = false;
    private const double TaskbarMargin = 8;

    private readonly MediaControlService _mediaControlService = new();
    private readonly TaskbarAnchorService _taskbarAnchorService = new();

    private readonly DispatcherTimer _repositionTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _trayGuardTimer;
    private readonly DispatcherTimer _visibilityGuardTimer;
    private readonly DispatcherTimer _fullscreenGuardTimer;
    private readonly DispatcherTimer _zOrderGuardTimer;

    private WinForms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private HwndSource? _hwndSource;
    private int _taskbarCreatedMessage;
    private bool _isExiting;
    private bool _isStatusUpdating;
    private bool _isUserHidden;
    private bool _isAutoHiddenForFullscreen;
    private bool _isFullscreenActive;
    private TaskbarAnchorService.RectD? _lastGoodAnchor;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Deactivated += MainWindow_Deactivated;
        StateChanged += MainWindow_StateChanged;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _repositionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += async (_, _) => await RefreshPlaybackUiAsync();
        _statusTimer.Start();

        _trayGuardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _trayGuardTimer.Tick += (_, _) => EnsureTrayIcon();
        _trayGuardTimer.Start();

        _visibilityGuardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _visibilityGuardTimer.Tick += (_, _) => EnsureWidgetVisible();
        _visibilityGuardTimer.Start();

        _fullscreenGuardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _fullscreenGuardTimer.Tick += (_, _) => HandleFullscreenState();
        _fullscreenGuardTimer.Start();

        _zOrderGuardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _zOrderGuardTimer.Tick += (_, _) => EnsureZOrder();
        _zOrderGuardTimer.Start();

        InitializeTrayIcon();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            RecreateTrayIcon();
        }

        return IntPtr.Zero;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mediaControlService.InitializeAsync();
            await RefreshPlaybackUiAsync();
            Reposition();
        }
        catch
        {
            // Global exception handlers in App.xaml.cs will log details.
        }
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = BuildTrayIcon();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowWidget());
        menu.Items.Add("Hide", null, (_, _) => HideWidget(true));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "Taskbar Music Widget",
            Icon = _trayIcon,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ToggleWidgetVisibility();
    }

    private void EnsureTrayIcon()
    {
        if (_isExiting)
        {
            return;
        }

        if (_notifyIcon == null)
        {
            InitializeTrayIcon();
            return;
        }

        if (!_notifyIcon.Visible)
        {
            _notifyIcon.Visible = true;
        }
    }

    private void RecreateTrayIcon()
    {
        if (_isExiting)
        {
            return;
        }

        InitializeTrayIcon();
    }

    private void ToggleWidgetVisibility()
    {
        if (IsVisible) HideWidget(true);
        else ShowWidget();
    }

    private void ShowWidget()
    {
        _isUserHidden = false;
        _isAutoHiddenForFullscreen = false;
        Show();
        WindowState = WindowState.Normal;
        Topmost = !_isFullscreenActive;
        EnsureZOrder();
        Reposition();
    }

    private void HideWidget(bool userInitiated)
    {
        _isUserHidden = userInitiated;
        Hide();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_isUserHidden)
        {
            return;
        }

        // Fixed mode: do not alter opacity when focus changes.
        Opacity = 1.0;
        if (!_isFullscreenActive)
        {
            Topmost = true;
            EnsureZOrder();
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_isUserHidden || _isFullscreenActive)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Show();
            Reposition();
        }

        if (!_isFullscreenActive)
        {
            EnsureZOrder();
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        if (_isUserHidden)
        {
            Hide();
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Reposition();
    }

    private void EnsureWidgetVisible()
    {
        if (_isExiting || _isUserHidden || _isFullscreenActive)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Reposition();
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Reposition();
        }

        if (!Topmost)
        {
            Topmost = true;
        }

        EnsureZOrder();
    }

    private void EnsureZOrder()
    {
        if (_isExiting || _isUserHidden || _isFullscreenActive || !IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging);
    }

    private void HandleFullscreenState()
    {
        if (_isExiting)
        {
            return;
        }

        var fullscreenNow = IsForegroundWindowFullscreen();
        _isFullscreenActive = fullscreenNow;

        if (fullscreenNow)
        {
            Topmost = false;
            if (!_isUserHidden && IsVisible)
            {
                _isAutoHiddenForFullscreen = true;
                HideWidget(false);
            }
            return;
        }

        if (_isAutoHiddenForFullscreen && !_isUserHidden)
        {
            _isAutoHiddenForFullscreen = false;
            Show();
            WindowState = WindowState.Normal;
            Topmost = true;
            EnsureZOrder();
            Reposition();
            return;
        }

        if (!_isUserHidden)
        {
            Topmost = true;
            EnsureZOrder();
        }
    }

    private bool IsForegroundWindowFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        var thisHandle = new WindowInteropHelper(this).Handle;
        if (fg == thisHandle)
        {
            return false;
        }

        if (!IsWindowVisible(fg) || !GetWindowRect(fg, out var fgRect))
        {
            return false;
        }

        var fgWidth = fgRect.Right - fgRect.Left;
        var fgHeight = fgRect.Bottom - fgRect.Top;
        if (fgWidth <= 0 || fgHeight <= 0)
        {
            return false;
        }

        var monitor = MonitorFromWindow(fg, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        var sameLeft = Math.Abs(fgRect.Left - monitorInfo.rcMonitor.Left) <= tolerance;
        var sameTop = Math.Abs(fgRect.Top - monitorInfo.rcMonitor.Top) <= tolerance;
        var sameRight = Math.Abs(fgRect.Right - monitorInfo.rcMonitor.Right) <= tolerance;
        var sameBottom = Math.Abs(fgRect.Bottom - monitorInfo.rcMonitor.Bottom) <= tolerance;

        return sameLeft && sameTop && sameRight && sameBottom;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Reposition();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        Reposition();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        Reposition();
    }

    private void Reposition()
    {
        if (!IsVisible)
        {
            return;
        }

        if (_hardFixedMode)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 8;
            Opacity = 1.0;
            EnsureZOrder();
            return;
        }

        if (_taskbarAnchorService.IsOverflowFlyoutOpen())
        {
            return;
        }

        var trayRect = _taskbarAnchorService.GetTrayAnchorRect();
        var taskbarRect = _taskbarAnchorService.GetTaskbarRect();

        var hasValidTrayAnchor = trayRect is { Width: > 40, Height: > 20 };
        if (hasValidTrayAnchor)
        {
            _lastGoodAnchor = trayRect;
        }

        var anchor = _lastGoodAnchor ?? (hasValidTrayAnchor ? trayRect : null) ?? taskbarRect;
        if (anchor == null)
        {
            return;
        }

        var isHorizontalTaskbar = anchor.Value.Width >= anchor.Value.Height;
        double targetLeft;
        double targetTop;

        if (isHorizontalTaskbar)
        {
            targetLeft = (hasValidTrayAnchor || _lastGoodAnchor != null)
                ? anchor.Value.Left - Width - TaskbarMargin
                : anchor.Value.Left + anchor.Value.Width - Width - TaskbarMargin;

            targetTop = anchor.Value.Top + Math.Max(0, (anchor.Value.Height - Height) / 2.0);
        }
        else
        {
            // Vertical taskbar fallback: pin near lower part of taskbar strip.
            targetLeft = anchor.Value.Left + Math.Max(0, (anchor.Value.Width - Width) / 2.0);
            targetTop = anchor.Value.Top + anchor.Value.Height - Height - TaskbarMargin;
        }

        var minX = SystemParameters.VirtualScreenLeft;
        var minY = SystemParameters.VirtualScreenTop;
        var maxX = minX + SystemParameters.VirtualScreenWidth - Width;
        var maxY = minY + SystemParameters.VirtualScreenHeight - Height;

        Left = Math.Clamp(targetLeft, minX, maxX);
        Top = Math.Clamp(targetTop, minY, maxY);
        Opacity = 1.0;
        EnsureZOrder();
    }

    private async System.Threading.Tasks.Task RefreshPlaybackUiAsync()
    {
        if (_isStatusUpdating)
        {
            return;
        }

        _isStatusUpdating = true;
        try
        {
            var state = await _mediaControlService.GetSnapshotAsync();

            PrevButton.IsEnabled = state.CanPrevious;
            ToggleButton.IsEnabled = state.CanTogglePlayPause;
            NextButton.IsEnabled = state.CanNext;

            ToggleIconText.Text = state.IsPlaying ? "⏸" : "▶";
        }
        finally
        {
            _isStatusUpdating = false;
        }
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaControlService.PreviousAsync();
        await RefreshPlaybackUiAsync();
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaControlService.TogglePlayPauseAsync();
        await RefreshPlaybackUiAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaControlService.NextAsync();
        await RefreshPlaybackUiAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _repositionTimer.Stop();
        _statusTimer.Stop();
        _trayGuardTimer.Stop();
        _visibilityGuardTimer.Stop();
        _fullscreenGuardTimer.Stop();
        _zOrderGuardTimer.Stop();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        base.OnClosed(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;
    private static readonly IntPtr HwndTopmost = new(-1);

    private static System.Drawing.Icon BuildTrayIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            graphics.FillEllipse(shadowBrush, 6, 8, 52, 52);
        }

        using (var baseBrush = new SolidBrush(Color.FromArgb(36, 38, 44)))
        {
            graphics.FillEllipse(baseBrush, 4, 6, 52, 52);
        }

        using (var ringPen = new Pen(Color.FromArgb(82, 165, 255), 3f))
        {
            graphics.DrawEllipse(ringPen, 6, 8, 48, 48);
        }

        PointF[] playTriangle =
        {
            new PointF(26, 22),
            new PointF(26, 42),
            new PointF(42, 32)
        };

        using (var playBrush = new SolidBrush(Color.FromArgb(240, 245, 255)))
        {
            graphics.FillPolygon(playBrush, playTriangle);
        }

        using (var accentBrush = new SolidBrush(Color.FromArgb(255, 114, 94)))
        {
            graphics.FillEllipse(accentBrush, 12, 12, 8, 8);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var tempIcon = System.Drawing.Icon.FromHandle(iconHandle);
            return (System.Drawing.Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}