using System.Text;
using System.Text.Json;

namespace Gomoku.Core.Configuration;

/// <summary>设置持久化：将 <see cref="AppSettings"/> 以 JSON 保存到用户目录，并支持容错加载。</summary>
public static class SettingsStore
{
    /// <summary>设置目录：%AppData%\Gomoku。</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Gomoku");

    /// <summary>游客（未登录）使用的设置文件：%AppData%\Gomoku\settings.json。</summary>
    public static string DefaultPath { get; } = Path.Combine(RootDirectory, "settings.json");

    /// <summary>按账号取设置文件：游客用 settings.json，账号用 settings.&lt;用户名&gt;.json，实现账号间界面偏好隔离。</summary>
    public static string PathFor(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return DefaultPath;
        }

        return Path.Combine(RootDirectory, $"settings.{Sanitize(nickname)}.json");
    }

    /// <summary>把用户名转换成安全的文件名片段：只保留中英文、数字、下划线。</summary>
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

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
