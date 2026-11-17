using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gomoku.Core.Configuration;
using Gomoku.Core.Networking;

namespace Gomoku.App;

/// <summary>一次认证的结果：成功时带账号信息，失败时带中文原因；CredentialInvalid 表示令牌已失效。</summary>
public sealed record AuthOutcome(bool Ok, string Message, AuthInfo? Info, bool CredentialInvalid = false);

/// <summary>账号信息：用户名、战绩与登录令牌。</summary>
public sealed record AuthInfo(string User, int Wins, int Losses, int Draws, string Token)
{
    /// <summary>胜率百分比（没打过按 50% 显示）。</summary>
    public int Rate => Wins + Losses == 0 ? 50 : (int)Math.Round(Wins * 100.0 / (Wins + Losses));
}

/// <summary>
/// 账号会话：把登录令牌保存在 %AppData%\Gomoku\session.json，实现"关掉程序再打开仍是登录态"；
/// 登录/退出时同时切换 App.Settings 到该账号的设置文件，让界面偏好按账号隔离。
/// </summary>
public static class AccountSession
{
    private const string DefaultServer = "127.0.0.1:9527";
    private const int TimeoutMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string? User { get; private set; }

    public static string? Token { get; private set; }

    public static int Wins { get; private set; }

    public static int Losses { get; private set; }

    public static int Draws { get; private set; }

    /// <summary>上次使用的服务器地址（"主机:端口"），登录面板会预填。</summary>
    public static string Server { get; set; } = DefaultServer;

    public static bool IsLoggedIn => User is not null && Token is not null;

    /// <summary>当前账号胜率百分比（没打过按 50% 显示）。</summary>
    public static int Rate => Wins + Losses == 0 ? 50 : (int)Math.Round(Wins * 100.0 / (Wins + Losses));

    private static string SessionPath => Path.Combine(SettingsStore.RootDirectory, "session.json");

    /// <summary>启动时读取上次登录的账号与令牌（不自动连服务器，由主界面决定何时恢复）。</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(SessionPath))
            {
                return;
            }

            string json = File.ReadAllText(SessionPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            User = ReadString(root, "user");
            Token = ReadString(root, "token");
            Server = ReadString(root, "server") ?? DefaultServer;
            Wins = ReadInt(root, "wins");
            Losses = ReadInt(root, "losses");
            Draws = ReadInt(root, "draws");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 会话文件损坏时按游客启动，不影响游戏
            User = null;
            Token = null;
        }
    }

    /// <summary>登录成功后落库：记录账号与令牌、切换为该账号的设置文件。</summary>
    public static void Apply(AuthInfo info)
    {
        User = info.User;
        Token = info.Token;
        Wins = info.Wins;
        Losses = info.Losses;
        Draws = info.Draws;
        Save();
        App.SwitchSettings(info.User);
    }

    /// <summary>退出登录：清空令牌与账号，切回游客设置。</summary>
    public static void Clear()
    {
        User = null;
        Token = null;
        Wins = 0;
        Losses = 0;
        Draws = 0;
        TryDelete(SessionPath);
        App.SwitchSettings(null);
    }

    /// <summary>战绩变化时更新本地缓存（对局结束、自动登录刷新）。</summary>
    public static void UpdateStats(int wins, int losses, int draws)
    {
        Wins = wins;
        Losses = losses;
        Draws = draws;
        if (IsLoggedIn)
        {
            Save();
        }
    }

    /// <summary>登录（password）或注册（register=true）：连服务器认证一次，成功后返回账号信息。</summary>
    public static async Task<AuthOutcome> SignInAsync(
        string server, string user, string password, bool register, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["user"] = user,
            ["pass"] = password
        };
        return await RequestAsync(server, register ? GameProtocol.Register : GameProtocol.Login, body, ct)
            .ConfigureAwait(true);
    }

    /// <summary>用本地保存的令牌恢复登录态；令牌失效时返回失败原因。</summary>
    public static async Task<AuthOutcome> RestoreAsync(string server, string token, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["token"] = token };
        return await RequestAsync(server, GameProtocol.LoginToken, body, ct).ConfigureAwait(true);
    }

    /// <summary>通知服务器作废当前令牌（离线失败可忽略，本地仍会清除）。</summary>
    public static async Task NotifyLogoutAsync(CancellationToken ct = default)
    {
        if (Token is null)
        {
            return;
        }

        await RequestAsync(Server, GameProtocol.Logout, new Dictionary<string, object?>(), ct).ConfigureAwait(true);
    }

    /// <summary>发一条认证消息并等待回包：auth_ok 成功，auth_fail/error 失败，5 秒超时。</summary>
    private static async Task<AuthOutcome> RequestAsync(
        string server, string type, Dictionary<string, object?> body, CancellationToken ct)
    {
        if (!TryParseServer(server, out string host, out int port))
        {
            return new AuthOutcome(false, "服务器地址格式应为 主机:端口，例如 127.0.0.1:9527", null);
        }

        var client = new GameClient();
        var completion = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.MessageReceived += (_, message) =>
        {
            switch (message.Type)
            {
                case GameProtocol.AuthOk:
                    completion.TrySetResult(new AuthOutcome(true, "登录成功", ReadAuth(message)));
                    break;
                case GameProtocol.AuthFail:
                    completion.TrySetResult(new AuthOutcome(
                        false,
                        GameProtocol.GetString(message, "message") ?? "登录失败",
                        null,
                        CredentialInvalid: true));
                    break;
                case GameProtocol.Error:
                    completion.TrySetResult(new AuthOutcome(false, GameProtocol.GetString(message, "message") ?? "操作失败", null));
                    break;
                case GameProtocol.LoggedOut:
                    completion.TrySetResult(new AuthOutcome(true, "已退出登录", null));
                    break;
            }
        };

        client.ConnectionClosed += (_, reason) => completion.TrySetResult(new AuthOutcome(false, reason, null));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeoutMs);

            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(true);
            if (!client.Send(type, body))
            {
                return new AuthOutcome(false, "发送登录请求失败，请重试", null);
            }

            Task<AuthOutcome> wait = completion.Task;
            Task delay = Task.Delay(TimeoutMs, timeout.Token);
            Task finished = await Task.WhenAny(wait, delay).ConfigureAwait(true);
            if (finished != wait)
            {
                return new AuthOutcome(false, $"连接服务器超时（{host}:{port}）", null);
            }

            return await wait.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return new AuthOutcome(false, "登录已取消", null);
        }
        catch (SocketException)
        {
            return new AuthOutcome(false, $"连不上服务器 {host}:{port}，请确认服务端已启动", null);
        }
        catch (IOException)
        {
            return new AuthOutcome(false, $"连不上服务器 {host}:{port}，请确认服务端已启动", null);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static AuthInfo? ReadAuth(NetworkMessage message)
    {
        string? user = GameProtocol.GetString(message, "user");
        if (user is null)
        {
            return null;
        }

        return new AuthInfo(
            user,
            GameProtocol.GetInt(message, "wins"),
            GameProtocol.GetInt(message, "losses"),
            GameProtocol.GetInt(message, "draws"),
            GameProtocol.GetString(message, "token") ?? string.Empty);
    }

    /// <summary>解析 "主机:端口"；省略端口时用 9527。</summary>
    public static bool TryParseServer(string server, out string host, out int port)
    {
        host = string.Empty;
        port = 9527;

        string text = (server ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        int colon = text.LastIndexOf(':');
        if (colon > 0)
        {
            host = text[..colon].Trim();
            string portText = text[(colon + 1)..].Trim();
            return host.Length > 0 && int.TryParse(portText, out port) && port is > 0 and < 65536;
        }

        host = text;
        return true;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;

    private static int ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement node) && node.ValueKind == JsonValueKind.Number
            ? node.GetInt32()
            : 0;

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.RootDirectory);
            var data = new Dictionary<string, object?>
            {
                ["user"] = User,
                ["token"] = Token,
                ["server"] = Server,
                ["wins"] = Wins,
                ["losses"] = Losses,
                ["draws"] = Draws
            };
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (IOException)
        {
            // 写不了会话文件时本次仍是登录态，只是下次需要重新登录
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 删不掉就留着，下次启动会因令牌失效而退回游客
        }
    }
}
