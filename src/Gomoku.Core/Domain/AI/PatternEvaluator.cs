using Gomoku.Core.Domain.Models;

namespace Gomoku.Core.Domain.AI;

/// <summary>
/// 棋型评估：沿四个方向扫描连子并打分（五连、活四、冲四、活三、眠三、活二、眠二），
/// 同时提供候选点生成。
/// </summary>
public static class PatternEvaluator
{
    // 分值表：数量级拉开差距，保证"先赢"与"防守"优先于普通进攻
    public const int FiveScore = 1_000_000;   // 五连（必胜）
    public const int OpenFourScore = 100_000; // 活四（双端点空，必胜）
    public const int FourScore = 10_000;      // 冲四（一端被堵）
    public const int OpenThreeScore = 8_000;  // 活三
    public const int ThreeScore = 500;        // 眠三
    public const int OpenTwoScore = 500;      // 活二
    public const int TwoScore = 50;           // 眠二

    private static readonly (int Dr, int Dc)[] Directions =
        { (0, 1), (1, 0), (1, 1), (1, -1) };

    /// <summary>以 player 视角评估整个局面：己方得分 − 对方得分（对方威胁越大分数越低）。</summary>
    public static int Evaluate(Board board, Stone player)
    {
        Stone opp = Opponent(player);
        return SumScore(board, player) - SumScore(board, opp);
    }

    /// <summary>player 一方的棋型总分（不扣对方分）。遍历当前可见窗口；连子可向窗口外延伸。</summary>
    public static int SumScore(Board board, Stone player)
    {
        int total = 0;
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                if (board.GetCell(row, col) != player)
                {
                    continue;
                }

                foreach ((int dr, int dc) in Directions)
                {
                    // 仅统计连子段首，避免同一连子重复计分（连子可跨窗口延伸）
                    int backRow = row - dr, backCol = col - dc;
                    if (board.GetCell(backRow, backCol) == player)
                    {
                        continue;
                    }

                    int length = 1;
                    int curRow = row + dr, curCol = col + dc;
                    while (board.GetCell(curRow, curCol) == player)
                    {
                        length++;
                        curRow += dr;
                        curCol += dc;
                    }

                    // 开放端：该方向端点在窗口内且为空（窗口外不可落子，视为堵）
                    bool backOpen = board.IsInView(backRow, backCol)
                                    && board.GetCell(backRow, backCol) == Stone.Empty;
                    bool frontOpen = board.IsInView(curRow, curCol)
                                     && board.GetCell(curRow, curCol) == Stone.Empty;
                    total += ScoreOf(length, backOpen, frontOpen);
                }
            }
        }

        return total;
    }

    /// <summary>生成候选落子点：可见窗口内已有棋子 2 格邻域中的空位（窗口为空时返回空集）。</summary>
    public static List<Position> GetCandidateMoves(Board board, int radius = 2)
    {
        var result = new List<Position>();
        var visited = new bool[Board.Size, Board.Size]; // 按窗口相对坐标去重
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                if (board.GetCell(row, col) == Stone.Empty)
                {
                    continue;
                }

                for (int dr = -radius; dr <= radius; dr++)
                {
                    for (int dc = -radius; dc <= radius; dc++)
                    {
                        int nr = row + dr, nc = col + dc;
                        int vi = nr - board.ViewRow, vj = nc - board.ViewCol;
                        if (vi < 0 || vi >= Board.Size || vj < 0 || vj >= Board.Size || visited[vi, vj])
                        {
                            continue;
                        }

                        if (board.IsEmpty(nr, nc))
                        {
                            visited[vi, vj] = true;
                            result.Add(new Position(nr, nc));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static int ScoreOf(int length, bool backOpen, bool frontOpen)
    {
        if (length >= 5)
        {
            return FiveScore;
        }

        if (backOpen && frontOpen)
        {
            return length switch
            {
                4 => OpenFourScore,
                3 => OpenThreeScore,
                2 => OpenTwoScore,
                _ => 2
            };
        }

        if (backOpen || frontOpen)
        {
            return length switch
            {
                4 => FourScore,
                3 => ThreeScore,
                2 => TwoScore,
                _ => 1
            };
        }

        return 0; // 两端被堵，无威胁
    }

    private static Stone Opponent(Stone stone)
        => stone == Stone.Black ? Stone.White : Stone.Black;
}
