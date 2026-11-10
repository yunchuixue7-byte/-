using Gomoku.Core.Domain.Models;

namespace Gomoku.Core.Domain.Rules;

/// <summary>规则引擎：空位校验、四向五连判定、平局判定。</summary>
public static class GameRules
{
    /// <summary>该位置是否可落子：在当前可见窗口内且为空位。</summary>
    public static bool CanPlace(Board board, Position pos)
        => board.IsInView(pos) && board.IsEmpty(pos);

    /// <summary>
    /// 在 pos 落下 stone 后是否构成五连。以落子点为中心，沿水平、垂直及两条对角线
    /// 共四个方向统计连续同色棋子数，任一方向达到 5 颗即判胜。
    /// 连子扫描使用世界坐标，可跨越可见窗口边界（移出窗口的旧棋仍计入连子）。
    /// </summary>
    public static bool IsWinningMove(Board board, Position pos, Stone stone)
    {
        if (stone == Stone.Empty || !board.IsInView(pos))
        {
            return false;
        }

        // 四个基础方向：横向、纵向、主对角线、副对角线
        foreach ((int Dr, int Dc) dir in new[] { (0, 1), (1, 0), (1, 1), (1, -1) })
        {
            int count = 1
                + CountInDirection(board, pos, dir.Dr, dir.Dc, stone)
                + CountInDirection(board, pos, -dir.Dr, -dir.Dc, stone);
            if (count >= 5)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 返回在 pos 落子后形成的五连坐标（含 pos 两端延伸）；未形成五连返回 null。
    /// 用于界面高亮获胜的五颗棋子。
    /// </summary>
    public static List<Position>? FindWinningLine(Board board, Position pos, Stone stone)
    {
        if (stone == Stone.Empty || !board.IsInView(pos))
        {
            return null;
        }

        foreach ((int Dr, int Dc) dir in new[] { (0, 1), (1, 0), (1, 1), (1, -1) })
        {
            var line = new List<Position> { pos };
            CollectAlong(board, pos, dir.Dr, dir.Dc, stone, line);
            CollectAlong(board, pos, -dir.Dr, -dir.Dc, stone, line);
            if (line.Count >= 5)
            {
                line.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
                return line;
            }
        }

        return null;
    }

    private static void CollectAlong(Board board, Position pos, int dr, int dc, Stone stone, List<Position> line)
    {
        int row = pos.Row + dr;
        int col = pos.Col + dc;
        while (board.GetCell(row, col) == stone) // 越界返回 Empty，自动停止
        {
            line.Add(new Position(row, col));
            row += dr;
            col += dc;
        }
    }

    /// <summary>当前可见窗口是否已下满（用于占满扩展/平局判定）。</summary>
    public static bool IsBoardFull(Board board)
    {
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                if (board.GetCell(row, col) == Stone.Empty)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>从 pos 出发沿 (dr, dc) 方向统计连续同色棋子数（不含起点）。</summary>
    private static int CountInDirection(Board board, Position pos, int dr, int dc, Stone stone)
    {
        int row = pos.Row + dr;
        int col = pos.Col + dc;
        int count = 0;

        while (board.GetCell(row, col) == stone) // 越界返回 Empty，自动停止
        {
            count++;
            row += dr;
            col += dc;
        }

        return count;
    }
}
