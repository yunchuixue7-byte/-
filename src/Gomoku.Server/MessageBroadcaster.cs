namespace Gomoku.Server;

/// <summary>消息广播：向对局双方转发落子、重开、结束等消息。</summary>
public static class MessageBroadcaster
{
    /// <summary>向房间内双方广播；某一方已断开时自动跳过。</summary>
    public static void Broadcast(Room room, string type, IReadOnlyDictionary<string, object?>? payload = null)
    {
        room.Black?.Send(type, payload);
        room.White?.Send(type, payload);
    }

    /// <summary>只发给指定的一方。</summary>
    public static void SendTo(ClientSession? session, string type, IReadOnlyDictionary<string, object?>? payload = null)
    {
        session?.Send(type, payload);
    }
}
