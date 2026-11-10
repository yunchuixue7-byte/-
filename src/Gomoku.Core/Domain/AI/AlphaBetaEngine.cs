using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;

namespace Gomoku.Core.Domain.AI;

/// <summary>
/// 困难档：带 Alpha-Beta 剪枝的 Negamax 搜索（深度 2–4）。
/// 落子点先按局面评分启发式排序，每层只取前 candidateLimit 个候选，
/// 避免搜索树指数膨胀（原实现未截断导致大师/巅峰档思考极慢）。
/// 支持 CancellationToken：认输/退出时可立即中止搜索，且中止时棋盘状态完整回滚。
/// </summary>
public sealed class AlphaBetaEngine : IPlayerEngine
{
    private readonly int _depth;
    private readonly int _candidateLimit;

    public AlphaBetaEngine() : this(depth: 3)
    {
    }

    public AlphaBetaEngine(int depth = 3, int candidateLimit = 12)
    {
        _depth = Math.Clamp(depth, 2, 4);
        _candidateLimit = Math.Max(1, candidateLimit);
    }

    public Position SelectMove(Board board, Stone myStone)
        => SelectMove(board, myStone, CancellationToken.None);

    public Position SelectMove(Board board, Stone myStone, CancellationToken cancellationToken = default)
    {
        // 空盘先手落窗口中心（天元）
        if (board.History.Count == 0)
        {
            return new Position(board.ViewRow + Board.Size / 2, board.ViewCol + Board.Size / 2);
        }

        List<Position> candidates = OrderCandidates(board, myStone, cancellationToken);
        if (candidates.Count == 0)
        {
            return new Position(board.ViewRow + Board.Size / 2, board.ViewCol + Board.Size / 2);
        }

        Stone opp = Opponent(myStone);
        int alpha = int.MinValue;
        Position best = candidates[0];
        int bestScore = int.MinValue;

        foreach (Position pos in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            board.PlaceStone(pos, myStone);
            int score;
            try
            {
                score = -Negamax(board, opp, _depth - 1, -int.MaxValue, -alpha, cancellationToken);
            }
            finally
            {
                board.UndoLast(); // 即使搜索中途被取消也保证棋盘回滚
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = pos;
            }

            if (bestScore > alpha)
            {
                alpha = bestScore;
            }
        }

        return best;
    }

    /// <summary>Negamax：返回当前行动方视角的局面分，带 Alpha-Beta 剪枝。</summary>
    private int Negamax(Board board, Stone toMove, int depth, int alpha, int beta, CancellationToken cancellationToken)
    {
        // 上一手已构成五连 → 上一手方获胜，当前行动方必败
        if (board.History.Count > 0)
        {
            MoveRecord last = board.History[^1];
            if (GameRules.IsWinningMove(board, last.Position, last.Stone))
            {
                return -int.MaxValue / 2;
            }
        }

        if (depth == 0)
        {
            return PatternEvaluator.Evaluate(board, toMove);
        }

        List<Position> candidates = OrderCandidates(board, toMove, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (Position pos in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            board.PlaceStone(pos, toMove);
            int score;
            try
            {
                score = -Negamax(board, Opponent(toMove), depth - 1, -beta, -alpha, cancellationToken);
            }
            finally
            {
                board.UndoLast(); // 即使搜索中途被取消也保证棋盘回滚
            }

            if (score > alpha)
            {
                alpha = score;
            }

            if (alpha >= beta)
            {
                break; // 剪枝
            }
        }

        return alpha;
    }

    /// <summary>按"落子后局面分"降序排序候选点（取前 _candidateLimit 个），提升剪枝效率。</summary>
    private List<Position> OrderCandidates(Board board, Stone player, CancellationToken cancellationToken)
    {
        return PatternEvaluator.GetCandidateMoves(board)
            .Select(pos =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                board.PlaceStone(pos, player);
                int score;
                try
                {
                    score = PatternEvaluator.Evaluate(board, player);
                }
                finally
                {
                    board.UndoLast();
                }
                return (Pos: pos, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Pos)
            .Take(_candidateLimit)
            .ToList();
    }

    private static Stone Opponent(Stone stone)
        => stone == Stone.Black ? Stone.White : Stone.Black;
}
