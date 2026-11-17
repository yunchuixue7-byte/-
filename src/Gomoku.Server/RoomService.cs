using System.IO;
using System.Net.Sockets;
using System.Text;
using Gomoku.Core.Domain.Models;
using Gomoku.Core.Networking;

namespace Gomoku.Server;

/// <summary>
/// 一个客户端连接的会话：持有套接字写端、所在房间与执子颜色。
/// 发送加锁，避免多个广播线程同时写同一连接导致消息交错。
/// </summary>
public sealed class ClientSession : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sendLock = new();
    private bool _closed;

    public ClientSession(TcpClient tcp)
    {
        Tcp = tcp;
        NetworkStream stream = tcp.GetStream();
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }

    public TcpClient Tcp { get; }

    /// <summary>执子颜色：0=黑，1=白；-1 表示尚未分配。</summary>
    public int Color { get; set; } = -1;

    /// <summary>所在房间编号；null 表示未进房间。</summary>
    public string? RoomCode { get; set; }

    /// <summary>发送一条消息；连接已关闭时返回 false。</summary>
    public bool Send(string type, IReadOnlyDictionary<string, object?>? payload = null)
    {
        if (_closed)
        {
            return false;
        }

        string line = GameProtocol.Encode(type, payload);
        lock (_sendLock)
        {
            try
            {
                _writer.WriteLine(line);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            _writer.Dispose();
        }
        catch (IOException)
        {
            // 连接已断，忽略
        }

        try
        {
            Tcp.Close();
        }
        catch (IOException)
        {
            // 忽略
        }
    }
}

/// <summary>一局联机对局：两名玩家、权威棋盘状态与胜负标记。</summary>
public sealed class Room
{
    public Room(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public ClientSession? Black { get; set; }

    public ClientSession? White { get; set; }

    /// <summary>权威棋盘：落子合法性、胜负判定都以服务端这份状态为准。</summary>
    public Board Board { get; set; } = new();

    /// <summary>双方是否都已就座（第二人加入后为 true）。</summary>
    public bool Started { get; set; }

    /// <summary>本局是否已结束。</summary>
    public bool Finished { get; set; }

    /// <summary>胜方颜色（0=黑，1=白）；平局为 -1。</summary>
    public int Winner { get; set; } = -1;

    /// <summary>认输方颜色；非认输结束为 -1。</summary>
    public int ResignedBy { get; set; } = -1;

    /// <summary>重开一局：清空棋盘、复位胜负标记，保留双方座位。</summary>
    public void ResetGame()
    {
        Board = new Board();
        Finished = false;
        Winner = -1;
        ResignedBy = -1;
    }

    /// <summary>房间内除指定会话外的另一名玩家。</summary>
    public ClientSession? OpponentOf(ClientSession session)
        => session == Black ? White : Black;
}

/// <summary>房间服务：管理房间的创建、加入、查找与销毁。所有操作在锁内进行。</summary>
public sealed class RoomService
{
    private readonly Dictionary<string, Room> _rooms = new();
    private readonly object _gate = new();
    private readonly Random _random = new();

    /// <summary>创建房间，创建者执黑；返回四位房间号。</summary>
    public string CreateRoom(ClientSession host)
    {
        lock (_gate)
        {
            string code;
            do
            {
                code = _random.Next(1000, 10000).ToString();
            }
            while (_rooms.ContainsKey(code));

            var room = new Room(code) { Black = host };
            host.Color = 0;
            host.RoomCode = code;
            _rooms[code] = room;
            return code;
        }
    }

    /// <summary>加入房间，加入者执白；房间不存在、已满或对局已开始时返回 null。</summary>
    public Room? TryJoin(string code, ClientSession guest, out string error)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out Room? room))
            {
                error = "房间不存在，请核对房间号";
                return null;
            }

            if (room.White is not null)
            {
                error = "房间已满";
                return null;
            }

            room.White = guest;
            guest.Color = 1;
            guest.RoomCode = code;
            error = string.Empty;
            return room;
        }
    }

    /// <summary>按房间号查找。</summary>
    public Room? Find(string code)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(code, out Room? room) ? room : null;
        }
    }

    /// <summary>会话所在房间；不在房间时返回 null。</summary>
    public Room? FindBySession(ClientSession session)
        => session.RoomCode is null ? null : Find(session.RoomCode);

    /// <summary>销毁房间（双方离开或对局结束后的清理）。</summary>
    public void Remove(Room room)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(room.Code, out Room? current) && current == room)
            {
                _rooms.Remove(room.Code);
            }
        }

        if (room.Black is not null)
        {
            room.Black.RoomCode = null;
            room.Black.Color = -1;
        }

        if (room.White is not null)
        {
            room.White.RoomCode = null;
            room.White.Color = -1;
        }
    }
}
