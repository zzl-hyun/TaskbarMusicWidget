using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
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
    private const double TaskbarMargin = 8;
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    private readonly MediaControlService _mediaControlService = new();
    private readonly TaskbarAnchorService _taskbarAnchorService = new();

    private readonly DispatcherTimer _repositionTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _trayGuardTimer;
    private readonly DispatcherTimer _visibilityGuardTimer;
    private readonly DispatcherTimer _fullscreenGuardTimer;
    private readonly DispatcherTimer _zOrderGuardTimer;

    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ContextMenuStrip? _trayMenu;
    private WinForms.ToolStripItem? _showMenuItem;
    private WinForms.ToolStripItem? _hideMenuItem;
    private readonly System.Windows.Controls.ToolTip _nowPlayingToolTip = new();
    private System.Drawing.Icon? _trayIcon;
    private HwndSource? _hwndSource;
    private int _taskbarCreatedMessage;
    private bool _isExiting;
    private bool _isStatusUpdating;
    private bool _isUserHidden;
    private bool _isAutoHiddenForFullscreen;
    private bool _isFullscreenActive;
    private TaskbarAnchorService.RectD? _lastGoodAnchor;
    private WinEventDelegate? _foregroundEventProc;
    private IntPtr _foregroundEventHook;

    public MainWindow()
    {
        InitializeComponent();
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;

        _nowPlayingToolTip.Content = "No active media session";
        _nowPlayingToolTip.PlacementTarget = WidgetRoot;
        _nowPlayingToolTip.Opened += OnNowPlayingToolTipOpened;
        WidgetRoot.ToolTip = _nowPlayingToolTip;
        WidgetRoot.ToolTipOpening += async (_, _) => await RefreshPlaybackUiAsync();

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
            Interval = TimeSpan.FromSeconds(2)
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
            Interval = TimeSpan.FromSeconds(2)
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
        InitializeForegroundHook();
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
            HandleFullscreenState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow_Loaded failed: {ex}");
            throw;
        }
    }

    private async void MainWindow_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta != 0)
        {
            var direction = e.Delta > 0 ? 1 : -1;
            await AdjustVolumeAsync(direction);
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task AdjustVolumeAsync(int direction)
    {
        try
        {
            var state = await _mediaControlService.GetSnapshotAsync();
            var targetName = NormalizeSessionName(state.SessionDisplayName);
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = device.AudioSessionManager;
            if (sessionManager is null)
            {
                return;
            }

            var sessions = sessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (session.IsSystemSoundsSession || session.State != AudioSessionState.AudioSessionStateActive)
                {
                    continue;
                }

                if (!SessionMatchesTarget(session, targetName))
                {
                    continue;
                }

                var volume = session.SimpleAudioVolume;
                var currentLevel = volume.Volume;
                var newLevel = Math.Clamp(currentLevel + (direction * 0.05f), 0f, 1f);
                volume.Volume = newLevel;
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Volume adjustment failed: {ex.Message}");
        }
    }

    private static bool SessionMatchesTarget(AudioSessionControl session, string targetName)
    {
        var displayName = NormalizeSessionName(session.DisplayName);
        if (!string.IsNullOrWhiteSpace(displayName) && (displayName == targetName || displayName.Contains(targetName) || targetName.Contains(displayName)))
        {
            return true;
        }

        var sessionIdentifier = NormalizeSessionName(session.GetSessionIdentifier);
        if (!string.IsNullOrWhiteSpace(sessionIdentifier) && (sessionIdentifier == targetName || sessionIdentifier.Contains(targetName) || targetName.Contains(sessionIdentifier)))
        {
            return true;
        }

        var instanceIdentifier = NormalizeSessionName(session.GetSessionInstanceIdentifier);
        return !string.IsNullOrWhiteSpace(instanceIdentifier) && (instanceIdentifier == targetName || instanceIdentifier.Contains(targetName) || targetName.Contains(instanceIdentifier));
    }

    private static string NormalizeSessionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = BuildTrayIcon();

        _trayMenu?.Dispose();
        _trayMenu = new WinForms.ContextMenuStrip();
        _showMenuItem = _trayMenu.Items.Add("Show", null, (_, _) => ShowWidget());
        _hideMenuItem = _trayMenu.Items.Add("Hide", null, (_, _) => HideWidget(true));
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        UpdateTrayMenuState();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "Taskbar Music Widget",
            Icon = _trayIcon,
            ContextMenuStrip = _trayMenu
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
        UpdateTrayMenuState();
    }

    private void HideWidget(bool userInitiated)
    {
        _isUserHidden = userInitiated;
        Hide();
        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (_showMenuItem is not null)
        {
            _showMenuItem.Enabled = !IsVisible;
        }

        if (_hideMenuItem is not null)
        {
            _hideMenuItem.Enabled = IsVisible;
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_isUserHidden)
        {
            return;
        }

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

    private void InitializeForegroundHook()
    {
        if (_foregroundEventHook != IntPtr.Zero)
        {
            return;
        }

        _foregroundEventProc = OnWinEventForegroundChanged;
        _foregroundEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _foregroundEventProc,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
    }

    private void OnWinEventForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_isExiting || eventType != EventSystemForeground)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(HandleFullscreenState), DispatcherPriority.Background);
    }

    private void OnNowPlayingToolTipOpened(object? sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(_nowPlayingToolTip) is not HwndSource source || source.Handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            source.Handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging);
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
        Dispatcher.BeginInvoke(new Action(Reposition));
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(Reposition));
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(Reposition));
    }

    private void Reposition()
    {
        if (!IsVisible)
        {
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

            ToggleIconText.Text = state.IsPlaying ? PauseGlyph : PlayGlyph;
            _nowPlayingToolTip.Content = BuildNowPlayingTooltip(state);
        }
        finally
        {
            _isStatusUpdating = false;
        }
    }

    private static string BuildNowPlayingTooltip(MediaControlService.PlaybackSnapshot state)
    {
        if (!state.HasSession)
        {
            return "No active media session";
        }

        if (string.IsNullOrWhiteSpace(state.TrackTitle))
        {
            return $"{state.SessionDisplayName}: No track metadata";
        }

        if (string.IsNullOrWhiteSpace(state.Artist))
        {
            return $"{state.SessionDisplayName}\n{state.TrackTitle}";
        }

        return $"{state.SessionDisplayName}\n{state.TrackTitle} - {state.Artist}";
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

        if (_foregroundEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundEventHook);
            _foregroundEventHook = IntPtr.Zero;
        }
        _foregroundEventProc = null;

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

        if (_trayMenu != null)
        {
            _trayMenu.Dispose();
            _trayMenu = null;
        }

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _mediaControlService.Dispose();

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

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;
    private static readonly IntPtr HwndTopmost = new(-1);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

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