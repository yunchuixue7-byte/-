using System.Text.Json;

namespace Gomoku.Core.Configuration;

/// <summary>设置持久化：将 <see cref="AppSettings"/> 以 JSON 保存到用户目录，并支持容错加载。</summary>
public static class SettingsStore
{
    /// <summary>默认设置文件路径：%AppData%\Gomoku\settings.json。</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Gomoku",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>加载设置；文件不存在或内容损坏时返回默认设置，不抛出异常。</summary>
    public static AppSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // 设置损坏时静默回退默认值，不影响游戏启动
            return new AppSettings();
        }
    }

    /// <summary>保存设置；目录不存在时自动创建。</summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(file, json);
        }
        catch
        {
            // 保存失败不影响当前对局，下次启动仍可用默认设置
        }
    }
}
