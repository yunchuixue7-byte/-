using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;

namespace Gomoku.Core.Domain.AI;

/// <summary>
/// 简单档：在候选点中优先直接取胜，否则按落子后局面评分，从高分池中随机选择。
/// poolSize 控制随机池大小：越大随机性越强（棋力越弱），1 为确定性选最优。
/// </summary>
public sealed class SimpleEngine : IPlayerEngine
{
    private readonly Random _random = new();
    private readonly int _poolSize;

    public SimpleEngine() : this(poolSize: 3)
    {
    }

    public SimpleEngine(int poolSize)
    {
        _poolSize = poolSize;
    }

    public Position SelectMove(Board board, Stone myStone, CancellationToken cancellationToken = default)
    {
        List<Position> candidates = PatternEvaluator.GetCandidateMoves(board);
        if (candidates.Count == 0)
        {
            return Center(board);
        }

        // 能直接五连就落子取胜
        foreach (Position pos in candidates)
        {
            if (WinsHere(board, pos, myStone))
            {
                return pos;
            }
        }

        // 按落子后我方局面分降序，从前 poolSize 名中随机选
        var scored = candidates
            .Select(pos => (Pos: pos, Score: ScoreAfter(board, pos, myStone)))
            .OrderByDescending(x => x.Score)
            .ToList();

        int pool = _poolSize > 0 && _poolSize <= scored.Count ? _poolSize : scored.Count;
        return scored[_random.Next(pool)].Pos;
    }

    /// <summary>模拟在 pos 落 stone 后的局面分（我方视角）。</summary>
    internal static int ScoreAfter(Board board, Position pos, Stone stone)
    {
        board.PlaceStone(pos, stone);
        int score = PatternEvaluator.Evaluate(board, stone);
        board.UndoLast();
        return score;
    }

    private static bool WinsHere(Board board, Position pos, Stone stone)
    {
        board.PlaceStone(pos, stone);
        bool win = GameRules.IsWinningMove(board, pos, stone);
        board.UndoLast();
        return win;
    }

    private static Position Center(Board board)
        => new(board.ViewRow + Board.Size / 2, board.ViewCol + Board.Size / 2);
}
