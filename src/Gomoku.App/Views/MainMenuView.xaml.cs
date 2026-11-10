using System.Windows;
using System.Windows.Controls;
using Gomoku.App;
using Gomoku.Core.Domain.AI;
using Gomoku.Core.Game;

namespace Gomoku.App.Views;

/// <summary>主菜单视图：国风启动界面，点击棋子按钮进入对应模式。</summary>
public partial class MainMenuView : UserControl
{
    private MainWindow? _window;
    private bool _isRestoring;

    public MainMenuView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => UnsubscribeFullScreen();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeFullScreen();
        RestoreSettings();
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

    private void Online_Click(object sender, RoutedEventArgs e)
        => StartGame(GameMode.Online);

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
