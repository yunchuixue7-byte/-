using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;

namespace Gomoku.Core.Domain.AI;

/// <summary>
/// 中等档：贪心选取攻防综合最高的位置。对每个候选点计算
/// "我落此处的局面分 − 对方落此处的局面分"，同时兼顾进攻与防守；
/// 能直接五连时必定优先取胜。
/// </summary>
public sealed class GreedyEngine : IPlayerEngine
{
    public Position SelectMove(Board board, Stone myStone, CancellationToken cancellationToken = default)
    {
        List<Position> candidates = PatternEvaluator.GetCandidateMoves(board);
        if (candidates.Count == 0)
        {
            return new Position(board.ViewRow + Board.Size / 2, board.ViewCol + Board.Size / 2);
        }

        Stone opp = Opponent(myStone);
        Position best = candidates[0];
        int bestScore = int.MinValue;

        foreach (Position pos in candidates)
        {
            // 必胜点直接返回
            if (WinsHere(board, pos, myStone))
            {
                return pos;
            }

            // 进攻分：我落此处的局面分（我方视角，越高越好）
            int attack = ScoreAfter(board, pos, myStone);
            // 防守分：对方落此处的局面分（我方视角，越低代表越危险，越应防守）
            int defense = ScoreAfterFrom(board, pos, opp, myStone);
            int total = attack - defense;

            if (total > bestScore)
            {
                bestScore = total;
                best = pos;
            }
        }

        return best;
    }

    /// <summary>模拟在 pos 落 stone 后返回从 player 视角评估的局面分。</summary>
    private static int ScoreAfter(Board board, Position pos, Stone stone)
    {
        board.PlaceStone(pos, stone);
        int score = PatternEvaluator.Evaluate(board, stone);
        board.UndoLast();
        return score;
    }

    /// <summary>模拟 stone 落在 pos 后，返回从 perspective 视角评估的局面分。</summary>
    private static int ScoreAfterFrom(Board board, Position pos, Stone stone, Stone perspective)
    {
        board.PlaceStone(pos, stone);
        int score = PatternEvaluator.Evaluate(board, perspective);
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

    private static Stone Opponent(Stone stone)
        => stone == Stone.Black ? Stone.White : Stone.Black;
}
