using System.Text.Json;

namespace Gomoku.Core.Networking;

/// <summary>联机消息协议：消息类型常量与 JSON 行编解码。</summary>
public static class GameProtocol
{
    // 客户端 → 服务端
    public const string CreateRoom = "create_room";
    public const string JoinRoom = "join_room";
    public const string Move = "move";
    public const string UndoRequest = "undo_request";
    public const string Restart = "restart";
    public const string Resign = "resign";
    public const string Leave = "leave";
    public const string Heartbeat = "heartbeat";

    // 账号与匹配
    public const string Register = "register";
    public const string Login = "login";
    public const string Logout = "logout";
    public const string LoginToken = "login_token";
    public const string QuickMatch = "quick_match";
    public const string CancelMatch = "cancel_match";

    // 服务端 → 客户端
    public const string RoomCreated = "room_created";
    public const string RoomJoined = "room_joined";
    public const string GameStarted = "start_game";
    public const string MoveApplied = "move_applied";
    public const string RestartApplied = "restart_applied";
    public const string GameOver = "game_over";
    public const string OpponentLeft = "opponent_left";
    public const string Error = "error";

    // 账号与匹配（下行）
    public const string AuthOk = "auth_ok";
    public const string AuthFail = "auth_fail";
    public const string LoggedOut = "logged_out";
    public const string MatchWaiting = "match_waiting";
    public const string MatchCanceled = "match_canceled";

    /// <summary>
    /// 把一条消息编码成单行 JSON：{"type":"move","payload":{"row":9,"col":9}}。
    /// 载荷只用简单键值（字符串/整数/布尔），避免多态序列化歧义。
    /// </summary>
    public static string Encode(string type, IReadOnlyDictionary<string, object?>? payload = null)
    {
        var message = new Dictionary<string, object?> { ["type"] = type };
        if (payload is not null)
        {
            message["payload"] = payload;
        }

        return JsonSerializer.Serialize(message);
    }

    /// <summary>解析一行 JSON；格式非法或缺少 type 时返回 false。</summary>
    public static bool TryDecode(string line, out NetworkMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement typeNode))
            {
                return false;
            }

            string? type = typeNode.GetString();
            if (string.IsNullOrEmpty(type))
            {
                return false;
            }

            JsonElement? payload = root.TryGetProperty("payload", out JsonElement payloadNode)
                ? payloadNode.Clone()
                : null;
            message = new NetworkMessage(type, payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读取载荷中的整数字段；缺失或类型不符时返回默认值。</summary>
    public static int GetInt(NetworkMessage message, string name, int fallback = 0)
    {
        if (message.Payload is null || !message.Payload.Value.TryGetProperty(name, out JsonElement node))
        {
            return fallback;
        }

        return node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int value) ? value : fallback;
    }

    /// <summary>读取载荷中的字符串字段；缺失时返回 null。</summary>
    public static string? GetString(NetworkMessage message, string name)
    {
        if (message.Payload is null || !message.Payload.Value.TryGetProperty(name, out JsonElement node))
        {
            return null;
        }

        return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    }

    /// <summary>读取载荷中的布尔字段；缺失时返回默认值。</summary>
    public static bool GetBool(NetworkMessage message, string name, bool fallback = false)
    {
        if (message.Payload is null || !message.Payload.Value.TryGetProperty(name, out JsonElement node))
        {
            return fallback;
        }

        return node.ValueKind is JsonValueKind.True or JsonValueKind.False ? node.GetBoolean() : fallback;
    }
}
