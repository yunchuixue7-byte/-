namespace Gomoku.Core.Networking;

/// <summary>联机消息协议：消息类型常量与编解码约定。</summary>
public static class GameProtocol
{
    public const string CreateRoom = "create_room";
    public const string JoinRoom = "join_room";
    public const string StartGame = "start_game";
    public const string Move = "move";
    public const string UndoRequest = "undo_request";
    public const string GameOver = "game_over";
    public const string Heartbeat = "heartbeat";

    // TODO(M4)：序列化 / 反序列化实现
}
