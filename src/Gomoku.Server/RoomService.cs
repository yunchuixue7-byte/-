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

    /// <summary>登录用户名；null 表示游客（不记战绩、不进账号匹配池）。</summary>
    public string? Nickname { get; set; }

    /// <summary>本次登录签发的令牌；退出登录时作废。</summary>
    public string? Token { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public bool IsGuest => Nickname is null;

    /// <summary>胜率；没打过棋的按 50% 计，与 Account 保持一致。</summary>
    public double WinRate => Wins + Losses == 0 ? 0.5 : (double)Wins / (Wins + Losses);

    /// <summary>对外展示的名字：登录用户显示用户名，游客显示"游客"。</summary>
    public string DisplayName => Nickname ?? "游客";

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
    private readonly List<ClientSession> _matchPool = new();
    private readonly object _gate = new();
    private readonly Random _random = new();

    /// <summary>创建房间，创建者执黑；返回四位房间号。</summary>
    public string CreateRoom(ClientSession host)
    {
        lock (_gate)
        {
            string code = NextCode();
            var room = new Room(code) { Black = host };
            host.Color = 0;
            host.RoomCode = code;
            _rooms[code] = room;
            return code;
        }
    }

    /// <summary>
    /// 把会话放入匹配池并尝试撮合：登录用户按胜率最接近配对，游客按先来先配。
    /// 配对成功返回新房间（partner 为对手），无人可配时返回 null，会话留在池中等待。
    /// </summary>
    public Room? TryMatch(ClientSession session, out ClientSession? partner)
    {
        partner = null;
        lock (_gate)
        {
            _matchPool.Remove(session); // 重复点匹配时不重复入池

            ClientSession? best = null;
            double bestGap = double.MaxValue;
            foreach (ClientSession other in _matchPool)
            {
                if (other.IsGuest != session.IsGuest)
                {
                    continue; // 游客与账号分开匹配，胜率无从比较
                }

                double gap = session.IsGuest ? 0 : Math.Abs(session.WinRate - other.WinRate);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = other;
                }
            }

            if (best is null)
            {
                _matchPool.Add(session);
                return null;
            }

            _matchPool.Remove(best);
            partner = best;

            string code = NextCode();
            var room = new Room(code) { Black = best, White = session };
            best.Color = 0;
            best.RoomCode = code;
            session.Color = 1;
            session.RoomCode = code;
            _rooms[code] = room;
            return room;
        }
    }

    /// <summary>把会话移出匹配池；返回是否原本在池中。</summary>
    public bool LeavePool(ClientSession session)
    {
        lock (_gate)
        {
            return _matchPool.Remove(session);
        }
    }

    private string NextCode()
    {
        string code;
        do
        {
            code = _random.Next(1000, 10000).ToString();
        }
        while (_rooms.ContainsKey(code));
        return code;
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
