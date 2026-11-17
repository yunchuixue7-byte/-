using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Gomoku.App;
using Gomoku.Core.Domain.AI;
using Gomoku.Core.Game;

namespace Gomoku.App.Views;

/// <summary>主菜单视图：国风启动界面，点击棋子按钮进入对应模式；右上角可登录账号。</summary>
public partial class MainMenuView : UserControl
{
    private MainWindow? _window;
    private bool _isRestoring;
    private bool _active;

    public MainMenuView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _active = false;
            UnsubscribeFullScreen();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _active = true;
        SubscribeFullScreen();
        AccountSession.Load();
        ServerBox.Text = AccountSession.Server;
        RestoreSettings();
        UpdateAccountUi();
        _ = TryRestoreSession();
    }

    /// <summary>启动时若本地存有登录令牌，自动连服务器恢复登录态（服务端没开时退回游客，不报错阻塞界面）。</summary>
    private async Task TryRestoreSession()
    {
        if (!AccountSession.IsLoggedIn)
        {
            return;
        }

        string expectedUser = AccountSession.User!;
        AccountText.Text = $"{expectedUser} · 正在恢复登录…";

        AuthOutcome outcome = await AccountSession.RestoreAsync(AccountSession.Server, AccountSession.Token!);
        if (!_active || AccountSession.User != expectedUser)
        {
            return; // 期间用户已手动登录/退出，或在已离开主菜单
        }

        if (outcome.Ok && outcome.Info is not null)
        {
            AccountSession.Apply(outcome.Info);
            RestoreSettings();
        }
        else if (outcome.CredentialInvalid)
        {
            AccountSession.Clear();
        }

        UpdateAccountUi();
    }

    /// <summary>刷新右上角账号文案：游客或"昵称 · 战绩 · 胜率"。</summary>
    private void UpdateAccountUi()
    {
        if (AccountSession.IsLoggedIn)
        {
            AccountText.Text = $"{AccountSession.User} · {AccountSession.Wins}胜{AccountSession.Losses}负"
                + $"{AccountSession.Draws}平 · 胜率{AccountSession.Rate}%";
            AccountButton.Content = "退出登录";
            return;
        }

        AccountText.Text = "游客 · 战绩不保存";
        AccountButton.Content = "登录 / 注册";
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        if (AccountSession.IsLoggedIn)
        {
            await LogoutAsync();
            return;
        }

        ServerBox.Text = AccountSession.Server;
        LoginStatus.Text = string.Empty;
        ColorPickPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        UserBox.Focus();
    }

    /// <summary>退出登录：通知服务器作废令牌、清本地会话、切回游客设置与游客界面偏好。</summary>
    private async Task LogoutAsync()
    {
        AccountText.Text = "正在退出登录…";
        AccountButton.IsEnabled = false;
        try
        {
            await AccountSession.NotifyLogoutAsync();
            AccountSession.Clear();
            if (_active)
            {
                RestoreSettings();
                UpdateAccountUi();
            }
        }
        finally
        {
            AccountButton.IsEnabled = true;
        }
    }

    private async void DoLogin_Click(object sender, RoutedEventArgs e) => await SubmitAsync(false);

    private async void DoRegister_Click(object sender, RoutedEventArgs e) => await SubmitAsync(true);

    private async Task SubmitAsync(bool register)
    {
        string server = ServerBox.Text.Trim();
        string user = UserBox.Text.Trim();
        string password = PassBox.Password;

        if (user.Length == 0 || password.Length == 0)
        {
            LoginStatus.Text = "请输入用户名和密码";
            return;
        }

        if (!AccountSession.TryParseServer(server, out _, out _))
        {
            LoginStatus.Text = "服务器地址格式应为 主机:端口，例如 127.0.0.1:9527";
            return;
        }

        LoginButton.IsEnabled = false;
        RegisterButton.IsEnabled = false;
        LoginStatus.Text = register ? "正在注册…" : "正在登录…";
        try
        {
            AuthOutcome outcome = await AccountSession.SignInAsync(server, user, password, register);
            if (!_active)
            {
                return;
            }

            if (!outcome.Ok || outcome.Info is null)
            {
                LoginStatus.Text = outcome.Message;
                return;
            }

            AccountSession.Server = server;
            AccountSession.Apply(outcome.Info);
            LoginPanel.Visibility = Visibility.Collapsed;
            UserBox.Text = string.Empty;
            PassBox.Password = string.Empty;
            LoginStatus.Text = string.Empty;
            RestoreSettings();
            UpdateAccountUi();
        }
        finally
        {
            LoginButton.IsEnabled = true;
            RegisterButton.IsEnabled = true;
        }
    }

    private void LoginCancel_Click(object sender, RoutedEventArgs e)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        LoginStatus.Text = string.Empty;
    }

    /// <summary>启动时恢复记忆的 AI 难度、棋盘材质与背景选择（旧版单一棋盘风格自动迁移）。</summary>
    private void RestoreSettings()
    {
        var s = App.Settings;
        _isRestoring = true;
        try
        {
            // 旧版"棋盘风格"单一下拉迁移：0/1 = 材质，2 = 动态视频背景；迁移后置 -1 不再覆盖新选择
            if (s.BoardSkinIndex == 0 || s.BoardSkinIndex == 1)
            {
                s.BoardMaterialIndex = s.BoardSkinIndex;
                s.BoardSkinIndex = -1;
                App.SaveSettings();
            }
            else if (s.BoardSkinIndex == 2)
            {
                s.BackgroundIndex = 1;
                s.BoardSkinIndex = -1;
                App.SaveSettings();
            }

            if (s.AiDifficultyIndex >= 0 && s.AiDifficultyIndex < DifficultyCombo.Items.Count)
            {
                DifficultyCombo.SelectedIndex = s.AiDifficultyIndex;
            }

            if (s.BoardMaterialIndex >= 0 && s.BoardMaterialIndex < BoardMaterialCombo.Items.Count)
            {
                BoardMaterialCombo.SelectedIndex = s.BoardMaterialIndex;
            }

            if (s.BackgroundIndex >= 0 && s.BackgroundIndex < BackgroundCombo.Items.Count)
            {
                BackgroundCombo.SelectedIndex = s.BackgroundIndex;
            }
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void DifficultyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoring || DifficultyCombo.SelectedIndex < 0)
        {
            return;
        }

        App.Settings.AiDifficultyIndex = DifficultyCombo.SelectedIndex;
        App.SaveSettings();
    }

    private void BoardMaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoring || BoardMaterialCombo.SelectedIndex < 0)
        {
            return;
        }

        App.Settings.BoardMaterialIndex = BoardMaterialCombo.SelectedIndex;
        App.SaveSettings();
    }

    private void BackgroundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoring || BackgroundCombo.SelectedIndex < 0)
        {
            return;
        }

        App.Settings.BackgroundIndex = BackgroundCombo.SelectedIndex;
        App.SaveSettings();
    }

    private void SubscribeFullScreen()
    {
        if (Window.GetWindow(this) is MainWindow w)
        {
            _window = w;
            w.FullScreenChanged += OnFullScreenChanged;
            OnFullScreenChanged(w, EventArgs.Empty);
        }
    }

    private void UnsubscribeFullScreen()
    {
        if (_window is not null)
        {
            _window.FullScreenChanged -= OnFullScreenChanged;
            _window = null;
        }
    }

    private void OnFullScreenChanged(object? sender, EventArgs e)
        => FullScreenButton.Content = (_window?.IsFullScreen ?? false) ? "还原" : "全屏";

    private void LocalTwoPlayer_Click(object sender, RoutedEventArgs e)
        => StartGame(GameMode.LocalTwoPlayer);

    private void VsAi_Click(object sender, RoutedEventArgs e)
        => ColorPickPanel.Visibility = Visibility.Visible;

    /// <summary>选择执黑（先行）：直接进入人机对战。</summary>
    private void PickBlack_Click(object sender, RoutedEventArgs e)
        => StartGame(GameMode.VsAi, 0);

    /// <summary>选择执白（后行）：AI 执黑先行。</summary>
    private void PickWhite_Click(object sender, RoutedEventArgs e)
        => StartGame(GameMode.VsAi, 1);

    private void ColorPickCancel_Click(object sender, RoutedEventArgs e)
        => ColorPickPanel.Visibility = Visibility.Collapsed;

    /// <summary>联机对战：进入大厅，连接服务器后建房或加入房间。</summary>
    private void Online_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.Navigate(new LobbyView());
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.ToggleFullScreen();

    private void Exit_Click(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    private void StartGame(GameMode mode, int humanColor = 0)
    {
        var controller = new GameController();
        if (mode == GameMode.VsAi)
        {
            controller.AiEngine = CreateAiEngine();
            controller.DifficultyLabel = (DifficultyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        }

        controller.StartSession(mode, mode == GameMode.VsAi ? humanColor : -1);
        if (Window.GetWindow(this) is MainWindow window)
        {
            string material = BoardMaterialCombo.SelectedIndex == 1 ? "stone" : "wood";
            bool videoBackground = BackgroundCombo.SelectedIndex == 1;
            window.Navigate(new GameBoardView(controller, material, videoBackground));
        }
    }

    /// <summary>按主菜单选择的难度创建 AI 引擎（下拉框顺序与 AIDifficulty 枚举一致，默认普通）。</summary>
    private IPlayerEngine CreateAiEngine()
        => AiEngineFactory.Create((AIDifficulty)DifficultyCombo.SelectedIndex);
}
