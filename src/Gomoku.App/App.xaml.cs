using System.Windows;
using Gomoku.Core.Configuration;

namespace Gomoku.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>当前应用设置（启动时为游客设置；登录后切换为该账号的设置）。</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>当前设置归属的账号名；null 表示游客（使用 settings.json）。</summary>
    public static string? SettingsAccount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = SettingsStore.Load(SettingsStore.PathFor(null));
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        base.OnExit(e);
    }

    /// <summary>切换到某个账号的设置文件（null = 游客）：先保存当前账号的改动，再载入目标账号的偏好。</summary>
    public static void SwitchSettings(string? nickname)
    {
        SaveSettings();

        SettingsAccount = string.IsNullOrWhiteSpace(nickname) ? null : nickname;
        Settings = SettingsStore.Load(SettingsStore.PathFor(SettingsAccount));
    }

    /// <summary>持久化当前设置到当前账号（或游客）的设置文件。</summary>
    public static void SaveSettings() => SettingsStore.Save(Settings, SettingsStore.PathFor(SettingsAccount));
}
