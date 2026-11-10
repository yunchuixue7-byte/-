using System.Windows;
using Gomoku.Core.Configuration;

namespace Gomoku.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>当前应用设置（启动时加载，变化即保存）。</summary>
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = SettingsStore.Load();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        base.OnExit(e);
    }

    /// <summary>持久化当前设置到用户目录。</summary>
    public static void SaveSettings() => SettingsStore.Save(Settings);
}

