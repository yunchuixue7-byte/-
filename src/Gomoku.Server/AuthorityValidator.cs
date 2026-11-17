using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;

namespace Gomoku.Server;

/// <summary>
/// 权威校验：服务端不相信客户端上报的棋盘，所有落子都校验「轮到谁、位置是否可下」。
/// 返回 null 表示通过，否则返回给客户端的中文错误提示。
/// </summary>
public static class AuthorityValidator
{
    public static string? ValidateMove(Room room, ClientSession session, int row, int col)
    {
        if (!room.Started)
        {
            return "对局尚未开始，等待对手加入";
        }

        if (room.Finished)
        {
            return "本局已结束，请先重开";
        }

        if (session.Color < 0)
        {
            return "你不在任何房间中";
        }

        if (session.Color != room.Board.CurrentPlayer)
        {
            return "还没轮到你落子";
        }

        if (!GameRules.CanPlace(room.Board, new Position(row, col)))
        {
            return "该位置不可落子（已有棋子或超出棋盘）";
        }

        return null;
    }

    /// <summary>校验重开请求：只有对局双方可以重开。</summary>
    public static string? ValidateRestart(Room room, ClientSession session)
    {
        if (session.Color < 0)
        {
            return "你不在任何房间中";
        }

        if (room.White is null)
        {
            return "对手尚未加入";
        }

        return null;
    }

    /// <summary>校验认输请求：只有对局进行中、且自己是房间成员时有效。</summary>
    public static string? ValidateResign(Room room, ClientSession session)
    {
        if (!room.Started || room.White is null)
        {
            return "对局尚未开始";
        }

        if (room.Finished)
        {
            return "本局已结束";
        }

        if (session.Color < 0)
        {
            return "你不在任何房间中";
        }

        return null;
    }
}
