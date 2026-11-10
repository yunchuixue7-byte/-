using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Gomoku.App;

/// <summary>主窗口：承载各视图，维护界面导航栈（支持逐级返回直至主菜单）。</summary>
public partial class MainWindow : Window
{
    private readonly Stack<UserControl> _history = new();
    private bool _isFullScreen;

    /// <summary>当前是否处于全屏。</summary>
    public bool IsFullScreen => _isFullScreen;

    /// <summary>全屏状态变化时触发（界面按钮据此切换"全屏/还原"文案）。</summary>
    public event EventHandler? FullScreenChanged;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        ApplyWindowState();
    }

    /// <summary>启动时恢复记忆的窗口状态（全屏或窗口几何）。</summary>
    private void ApplyWindowState()
    {
        var s = App.Settings;
        if (s.IsFullScreen)
        {
            ToggleFullScreen();
            return;
        }

        WindowState = WindowState.Normal;
        if (s.WindowLeft.HasValue)
        {
            Left = s.WindowLeft.Value;
        }

        if (s.WindowTop.HasValue)
        {
            Top = s.WindowTop.Value;
        }

        if (s.WindowWidth > 0)
        {
            Width = s.WindowWidth;
        }

        if (s.WindowHeight > 0)
        {
            Height = s.WindowHeight;
        }
    }

    /// <summary>F11 进入/退出全屏；全屏时 Esc 退出。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    /// <summary>切换无边框全屏（界面上的全屏按钮也调用此方法）。</summary>
    public void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;
        if (_isFullScreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
        }

        App.Settings.IsFullScreen = _isFullScreen;
        App.SaveSettings();
        FullScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>关闭前记录窗口几何。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isFullScreen)
        {
            App.Settings.WindowLeft = Left;
            App.Settings.WindowTop = Top;
            App.Settings.WindowWidth = ActualWidth;
            App.Settings.WindowHeight = ActualHeight;
            App.SaveSettings();
        }

        base.OnClosing(e);
    }

    /// <summary>导航到新视图，当前视图压入历史栈。</summary>
    public void Navigate(UserControl view)
    {
        if (Content is UserControl current && current != view)
        {
            _history.Push(current);
        }

        Content = view;
    }

    /// <summary>返回上一界面；无上一界面时不执行任何操作。</summary>
    public void GoBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        Content = _history.Pop();
    }

    /// <summary>是否存在可返回的上一界面。</summary>
    public bool CanGoBack => _history.Count > 0;
}
