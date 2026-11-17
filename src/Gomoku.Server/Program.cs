using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;
using Gomoku.Core.Networking;

namespace Gomoku.Server;

/// <summary>
/// 联机服务器入口：监听 TCP 端口，每个连接一个会话，消息按房间转发。
/// 服务端持有权威棋盘，落子合法性、胜负与平局都由此判定，客户端只负责显示。
/// 账号与战绩落 SQLite，匹配按胜率撮合。
/// </summary>
public static class Program
{
    private const int DefaultPort = 9527;

    public static async Task Main(string[] args)
    {
        int port = DefaultPort;
        if (args.Length > 0 && int.TryParse(args[0], out int parsed))
        {
            port = parsed;
        }

        string dbPath = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "gomoku.db");

        var service = new RoomService();
        var accounts = new AccountStore(dbPath);

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Gomoku.Server 已启动，监听端口 {port}（按 Ctrl+C 退出）");
        Console.WriteLine($"账号库：{dbPath}");

        while (true)
        {
            TcpClient tcp = await listener.AcceptTcpClientAsync();
            tcp.NoDelay = true;
            _ = Task.Run(() => HandleClientAsync(tcp, service, accounts));
        }
    }

    /// <summary>单个连接的生命周期：逐行读取消息，断开时清理房间并向对手广播。</summary>
    private static async Task HandleClientAsync(TcpClient tcp, RoomService service, AccountStore accounts)
    {
        using var session = new ClientSession(tcp);
        try
        {
            using var reader = new StreamReader(tcp.GetStream(), new UTF8Encoding(false));
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                if (!GameProtocol.TryDecode(line, out NetworkMessage? message) || message is null)
                {
                    continue;
                }

                HandleMessage(session, message, service, accounts);
            }
        }
        catch (IOException)
        {
            // 客户端异常断开，按正常离开处理
        }
        catch (SocketException)
        {
            // 同上
        }
        finally
        {
            HandleDisconnect(session, service);
        }
    }

    /// <summary>按消息类型分发；涉及棋局的操作都在房间锁内完成，避免两侧同时落子。</summary>
    private static void HandleMessage(ClientSession session, NetworkMessage message, RoomService service, AccountStore accounts)
    {
        switch (message.Type)
        {
            case GameProtocol.Register:
                Register(session, message, accounts);
                break;
            case GameProtocol.Login:
                Login(session, message, accounts);
                break;
            case GameProtocol.LoginToken:
                LoginByToken(session, message, accounts);
                break;
            case GameProtocol.Logout:
                Logout(session, service, accounts);
                break;
            case GameProtocol.QuickMatch:
                QuickMatch(session, service);
                break;
            case GameProtocol.CancelMatch:
                CancelMatch(session, service);
                break;
            case GameProtocol.CreateRoom:
                CreateRoom(session, service);
                break;
            case GameProtocol.JoinRoom:
                JoinRoom(session, message, service);
                break;
            case GameProtocol.Move:
                ApplyMove(session, message, service, accounts);
                break;
            case GameProtocol.Restart:
                Restart(session, service);
                break;
            case GameProtocol.Resign:
                Resign(session, service, accounts);
                break;
            case GameProtocol.Leave:
                Leave(session, service);
                break;
            case GameProtocol.Heartbeat:
                session.Send(GameProtocol.Heartbeat);
                break;
            default:
                session.Send(GameProtocol.Error, ErrorPayload("未知的消息类型"));
                break;
        }
    }

    // ---------- 账号 ----------

    private static void Register(ClientSession session, NetworkMessage message, AccountStore accounts)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("对局中不能注册，请先离开房间"));
            return;
        }

        if (session.Nickname is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("已登录，请先退出登录"));
            return;
        }

        string? name = GameProtocol.GetString(message, "user");
        string? pass = GameProtocol.GetString(message, "pass");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pass))
        {
            session.Send(GameProtocol.Error, ErrorPayload("请输入用户名和密码"));
            return;
        }

        Account? account = accounts.Register(name, pass, out string error);
        if (account is null)
        {
            session.Send(GameProtocol.AuthFail, ErrorPayload(error));
            return;
        }

        ApplyAccount(session, account);
        string token = accounts.CreateToken(account.Name);
        session.Token = token;
        session.Send(GameProtocol.AuthOk, AccountPayload(account, token));
    }

    private static void Login(ClientSession session, NetworkMessage message, AccountStore accounts)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("对局中不能登录，请先离开房间"));
            return;
        }

        if (session.Nickname is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("已登录，请先退出登录"));
            return;
        }

        string? name = GameProtocol.GetString(message, "user");
        string? pass = GameProtocol.GetString(message, "pass");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pass))
        {
            session.Send(GameProtocol.Error, ErrorPayload("请输入用户名和密码"));
            return;
        }

        Account? account = accounts.Login(name, pass, out string error);
        if (account is null)
        {
            session.Send(GameProtocol.AuthFail, ErrorPayload(error));
            return;
        }

        ApplyAccount(session, account);
        string token = accounts.CreateToken(account.Name);
        session.Token = token;
        session.Send(GameProtocol.AuthOk, AccountPayload(account, token));
    }

    /// <summary>用客户端本地保存的令牌自动登录（主界面启动与进入大厅时使用）。</summary>
    private static void LoginByToken(ClientSession session, NetworkMessage message, AccountStore accounts)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("对局中不能登录，请先离开房间"));
            return;
        }

        if (session.Nickname is not null)
        {
            // 已登录，直接回一次账号信息，便于客户端刷新界面
            Account? current = accounts.Get(session.Nickname);
            if (current is not null)
            {
                ApplyAccount(session, current);
                session.Send(GameProtocol.AuthOk, AccountPayload(current, session.Token ?? string.Empty));
                return;
            }

            session.Send(GameProtocol.AuthFail, ErrorPayload("登录状态已失效，请重新登录"));
            return;
        }

        string? token = GameProtocol.GetString(message, "token");
        if (string.IsNullOrWhiteSpace(token))
        {
            session.Send(GameProtocol.AuthFail, ErrorPayload("登录状态无效，请重新登录"));
            return;
        }

        Account? account = accounts.ResolveToken(token, out string error);
        if (account is null)
        {
            session.Send(GameProtocol.AuthFail, ErrorPayload(error));
            return;
        }

        ApplyAccount(session, account);
        session.Token = token;
        session.Send(GameProtocol.AuthOk, AccountPayload(account, token));
    }

    private static void Logout(ClientSession session, RoomService service, AccountStore accounts)
    {
        service.LeavePool(session);
        if (session.Token is not null)
        {
            accounts.DeleteToken(session.Token);
            session.Token = null;
        }

        session.Nickname = null;
        session.Wins = 0;
        session.Losses = 0;
        session.Draws = 0;
        session.Send(GameProtocol.LoggedOut);
    }

    private static void ApplyAccount(ClientSession session, Account account)
    {
        session.Nickname = account.Name;
        session.Wins = account.Wins;
        session.Losses = account.Losses;
        session.Draws = account.Draws;
    }

    private static Dictionary<string, object?> AccountPayload(Account account, string token)
        => new()
        {
            ["user"] = account.Name,
            ["wins"] = account.Wins,
            ["losses"] = account.Losses,
            ["draws"] = account.Draws,
            ["rate"] = Math.Round(account.WinRate * 100),
            ["token"] = token
        };

    // ---------- 匹配 ----------

    private static void QuickMatch(ClientSession session, RoomService service)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你已在房间中，请先离开"));
            return;
        }

        Room? room = service.TryMatch(session, out _);
        if (room is null)
        {
            session.Send(GameProtocol.MatchWaiting, new Dictionary<string, object?>
            {
                ["message"] = session.IsGuest
                    ? "已进入游客匹配队列，等待另一位游客…"
                    : "正在按胜率寻找对手…"
            });
            return;
        }

        lock (room)
        {
            room.ResetGame();
            room.Started = true;
            NotifyStart(room);
        }
    }

    private static void CancelMatch(ClientSession session, RoomService service)
    {
        bool removed = service.LeavePool(session);
        session.Send(GameProtocol.MatchCanceled, new Dictionary<string, object?>
        {
            ["removed"] = removed
        });
    }

    /// <summary>给房间双方各发一条开局消息，带上自己的执子颜色与双方昵称。</summary>
    private static void NotifyStart(Room room)
    {
        NotifyStartTo(room, room.Black);
        NotifyStartTo(room, room.White);
    }

    private static void NotifyStartTo(Room room, ClientSession? target)
    {
        target?.Send(GameProtocol.GameStarted, new Dictionary<string, object?>
        {
            ["color"] = target.Color,
            ["black"] = room.Black?.DisplayName ?? "游客",
            ["white"] = room.White?.DisplayName ?? "游客",
            ["blackFirst"] = true
        });
    }

    // ---------- 房间 ----------

    private static void CreateRoom(ClientSession session, RoomService service)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你已在房间中，请先离开"));
            return;
        }

        service.LeavePool(session);
        string code = service.CreateRoom(session);
        session.Send(GameProtocol.RoomCreated, new Dictionary<string, object?>
        {
            ["room"] = code,
            ["color"] = 0,
            ["name"] = session.DisplayName
        });
    }

    private static void JoinRoom(ClientSession session, NetworkMessage message, RoomService service)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你已在房间中，请先离开"));
            return;
        }

        string? raw = GameProtocol.GetString(message, "room");
        if (string.IsNullOrWhiteSpace(raw))
        {
            session.Send(GameProtocol.Error, ErrorPayload("请输入房间号"));
            return;
        }

        service.LeavePool(session);
        Room? room = service.TryJoin(raw.Trim(), session, out string error);
        if (room is null)
        {
            session.Send(GameProtocol.Error, ErrorPayload(error));
            return;
        }

        session.Send(GameProtocol.RoomJoined, new Dictionary<string, object?>
        {
            ["room"] = room.Code,
            ["color"] = 1
        });

        lock (room)
        {
            room.ResetGame();
            room.Started = true;
            NotifyStart(room);
        }
    }

    private static void ApplyMove(ClientSession session, NetworkMessage message, RoomService service, AccountStore accounts)
    {
        Room? room = service.FindBySession(session);
        if (room is null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你不在任何房间中"));
            return;
        }

        int row = GameProtocol.GetInt(message, "row", -1);
        int col = GameProtocol.GetInt(message, "col", -1);

        lock (room)
        {
            string? error = AuthorityValidator.ValidateMove(room, session, row, col);
            if (error is not null)
            {
                session.Send(GameProtocol.Error, ErrorPayload(error));
                return;
            }

            Stone stone = room.Board.CurrentPlayer == 0 ? Stone.Black : Stone.White;
            var position = new Position(row, col);
            if (!room.Board.PlaceStone(position, stone))
            {
                session.Send(GameProtocol.Error, ErrorPayload("落子失败，请重试"));
                return;
            }

            bool win = GameRules.IsWinningMove(room.Board, position, stone);
            bool full = GameRules.IsBoardFull(room.Board);
            string reason = "five";
            if (win)
            {
                room.Finished = true;
                room.Winner = stone == Stone.Black ? 0 : 1;
            }
            else if (full)
            {
                room.Finished = true;
                room.Winner = -1;
                reason = "full";
            }

            if (room.Finished)
            {
                RecordResult(room, accounts, reason);
            }

            MessageBroadcaster.Broadcast(room, GameProtocol.MoveApplied, new Dictionary<string, object?>
            {
                ["row"] = row,
                ["col"] = col,
                ["finished"] = room.Finished,
                ["winner"] = room.Winner,
                ["current"] = room.Board.CurrentPlayer
            });
        }
    }

    private static void Restart(ClientSession session, RoomService service)
    {
        Room? room = service.FindBySession(session);
        if (room is null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你不在任何房间中"));
            return;
        }

        lock (room)
        {
            string? error = AuthorityValidator.ValidateRestart(room, session);
            if (error is not null)
            {
                session.Send(GameProtocol.Error, ErrorPayload(error));
                return;
            }

            room.ResetGame();
            MessageBroadcaster.Broadcast(room, GameProtocol.RestartApplied);
        }
    }

    private static void Resign(ClientSession session, RoomService service, AccountStore accounts)
    {
        Room? room = service.FindBySession(session);
        if (room is null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你不在任何房间中"));
            return;
        }

        lock (room)
        {
            string? error = AuthorityValidator.ValidateResign(room, session);
            if (error is not null)
            {
                session.Send(GameProtocol.Error, ErrorPayload(error));
                return;
            }

            room.Finished = true;
            room.ResignedBy = session.Color;
            room.Winner = 1 - session.Color;
            RecordResult(room, accounts, "resign");
            MessageBroadcaster.Broadcast(room, GameProtocol.GameOver, new Dictionary<string, object?>
            {
                ["winner"] = room.Winner,
                ["resignedBy"] = room.ResignedBy,
                ["reason"] = "resign"
            });
        }
    }

    /// <summary>一局结束后写库：游客不记账号计数，但仍记入对局明细；同时刷新会话内胜率。</summary>
    private static void RecordResult(Room room, AccountStore accounts, string reason)
    {
        if (room.Black is null || room.White is null)
        {
            return;
        }

        accounts.RecordResult(
            room.Black.IsGuest ? null : room.Black.Nickname,
            room.White.IsGuest ? null : room.White.Nickname,
            room.Winner,
            reason);

        switch (room.Winner)
        {
            case 0:
                room.Black.Wins++;
                room.White.Losses++;
                break;
            case 1:
                room.White.Wins++;
                room.Black.Losses++;
                break;
            default:
                room.Black.Draws++;
                room.White.Draws++;
                break;
        }
    }

    private static void Leave(ClientSession session, RoomService service)
    {
        service.LeavePool(session);

        Room? room = service.FindBySession(session);
        if (room is null)
        {
            session.RoomCode = null;
            return;
        }

        ClientSession? opponent = room.OpponentOf(session);
        service.Remove(room);
        opponent?.Send(GameProtocol.OpponentLeft, ErrorPayload("对手已离开房间"));
    }

    /// <summary>连接断开：移出匹配池、清房间并通知对手。</summary>
    private static void HandleDisconnect(ClientSession session, RoomService service)
    {
        service.LeavePool(session);

        Room? room = service.FindBySession(session);
        if (room is null)
        {
            return;
        }

        ClientSession? opponent = room.OpponentOf(session);
        service.Remove(room);
        opponent?.Send(GameProtocol.OpponentLeft, ErrorPayload("对手已断开连接"));
    }

    private static Dictionary<string, object?> ErrorPayload(string message)
        => new() { ["message"] = message };
}
