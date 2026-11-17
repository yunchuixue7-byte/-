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

        var service = new RoomService();
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Gomoku.Server 已启动，监听端口 {port}（按 Ctrl+C 退出）");

        while (true)
        {
            TcpClient tcp = await listener.AcceptTcpClientAsync();
            tcp.NoDelay = true;
            _ = Task.Run(() => HandleClientAsync(tcp, service));
        }
    }

    /// <summary>单个连接的生命周期：逐行读取消息，断开时清理房间并向对手广播。</summary>
    private static async Task HandleClientAsync(TcpClient tcp, RoomService service)
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

                HandleMessage(session, message, service);
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
    private static void HandleMessage(ClientSession session, NetworkMessage message, RoomService service)
    {
        switch (message.Type)
        {
            case GameProtocol.CreateRoom:
                CreateRoom(session, service);
                break;
            case GameProtocol.JoinRoom:
                JoinRoom(session, message, service);
                break;
            case GameProtocol.Move:
                ApplyMove(session, message, service);
                break;
            case GameProtocol.Restart:
                Restart(session, service);
                break;
            case GameProtocol.Resign:
                Resign(session, service);
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

    private static void CreateRoom(ClientSession session, RoomService service)
    {
        if (session.RoomCode is not null)
        {
            session.Send(GameProtocol.Error, ErrorPayload("你已在房间中，请先离开"));
            return;
        }

        string code = service.CreateRoom(session);
        session.Send(GameProtocol.RoomCreated, new Dictionary<string, object?>
        {
            ["room"] = code,
            ["color"] = 0
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
            MessageBroadcaster.Broadcast(room, GameProtocol.GameStarted, new Dictionary<string, object?>
            {
                ["blackFirst"] = true
            });
        }
    }

    private static void ApplyMove(ClientSession session, NetworkMessage message, RoomService service)
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
            if (win)
            {
                room.Finished = true;
                room.Winner = stone == Stone.Black ? 0 : 1;
            }
            else if (full)
            {
                room.Finished = true;
                room.Winner = -1;
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

    private static void Resign(ClientSession session, RoomService service)
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
            MessageBroadcaster.Broadcast(room, GameProtocol.GameOver, new Dictionary<string, object?>
            {
                ["winner"] = room.Winner,
                ["resignedBy"] = room.ResignedBy,
                ["reason"] = "resign"
            });
        }
    }

    private static void Leave(ClientSession session, RoomService service)
    {
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

    /// <summary>连接断开：清房间并通知对手。</summary>
    private static void HandleDisconnect(ClientSession session, RoomService service)
    {
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
