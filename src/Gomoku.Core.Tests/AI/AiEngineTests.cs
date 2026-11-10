using Gomoku.Core.Domain.AI;
using Gomoku.Core.Domain.Models;
using Xunit;

namespace Gomoku.Core.Tests.AI;

/// <summary>AI 引擎用例：棋型评分、三档引擎的取胜与防守行为。</summary>
public class AiEngineTests
{
    [Fact]
    public void Engine_ImplementsInterface()
    {
        IPlayerEngine engine = new SimpleEngine();
        Assert.NotNull(engine);
    }

    [Fact]
    public void PatternEvaluator_FiveScores_HigherThan_OpenThree()
    {
        var board = new Board();
        // 黑五连
        for (int col = 3; col <= 7; col++)
        {
            board.PlaceStone(new Position(7, col), Stone.Black);
        }

        int fiveScore = PatternEvaluator.Evaluate(board, Stone.Black);

        board.Reset();
        // 黑活三（两端均空）
        for (int col = 4; col <= 6; col++)
        {
            board.PlaceStone(new Position(7, col), Stone.Black);
        }

        int threeScore = PatternEvaluator.Evaluate(board, Stone.Black);
        Assert.True(fiveScore > threeScore);
        Assert.True(threeScore > 0);
    }

    [Fact]
    public void PatternEvaluator_OpenThree_ScoresHigherThan_SleepingThree()
    {
        var board = new Board();
        // 活三：两端空
        board.PlaceStone(new Position(7, 4), Stone.Black);
        board.PlaceStone(new Position(7, 5), Stone.Black);
        board.PlaceStone(new Position(7, 6), Stone.Black);
        int openScore = PatternEvaluator.Evaluate(board, Stone.Black);

        board.Reset();
        // 眠三：一端被白堵
        board.PlaceStone(new Position(7, 3), Stone.White);
        board.PlaceStone(new Position(7, 4), Stone.Black);
        board.PlaceStone(new Position(7, 5), Stone.Black);
        board.PlaceStone(new Position(7, 6), Stone.Black);
        int blockedScore = PatternEvaluator.Evaluate(board, Stone.Black);

        Assert.True(openScore > blockedScore);
    }

    [Fact]
    public void CandidateMoves_Contains_EmptyNeighbors_ButNot_Occupied()
    {
        var board = new Board();
        board.PlaceStone(new Position(7, 7), Stone.Black);

        List<Position> candidates = PatternEvaluator.GetCandidateMoves(board);

        Assert.Contains(new Position(7, 8), candidates);
        Assert.Contains(new Position(6, 6), candidates);
        Assert.DoesNotContain(new Position(7, 7), candidates); // 已占位
        Assert.DoesNotContain(new Position(0, 0), candidates);  // 远离棋子
    }

    [Theory]
    [InlineData(typeof(SimpleEngine))]
    [InlineData(typeof(GreedyEngine))]
    [InlineData(typeof(AlphaBetaEngine))]
    public void Engine_Completes_Own_FourInARow(Type engineType)
    {
        var board = new Board();
        for (int col = 3; col <= 6; col++)
        {
            board.PlaceStone(new Position(7, col), Stone.Black);
        }

        var engine = (IPlayerEngine)Activator.CreateInstance(engineType)!;
        Position move = engine.SelectMove(board, Stone.Black);

        Assert.True(
            move == new Position(7, 2) || move == new Position(7, 7),
            $"期望补足五连，实际选择 {move}");
    }

    [Fact]
    public void Greedy_Blocks_Opponent_FourInARow()
    {
        var board = new Board();
        // 白方四连，黑方 AI 必须堵住
        for (int col = 3; col <= 6; col++)
        {
            board.PlaceStone(new Position(7, col), Stone.White);
        }

        var engine = new GreedyEngine();
        Position move = engine.SelectMove(board, Stone.Black);

        Assert.True(
            move == new Position(7, 2) || move == new Position(7, 7),
            $"期望堵住白方四连，实际选择 {move}");
    }

    [Fact]
    public void AlphaBeta_Blocks_Opponent_FourInARow()
    {
        var board = new Board();
        for (int col = 3; col <= 6; col++)
        {
            board.PlaceStone(new Position(7, col), Stone.White);
        }

        var engine = new AlphaBetaEngine(depth: 3);
        Position move = engine.SelectMove(board, Stone.Black);

        Assert.True(
            move == new Position(7, 2) || move == new Position(7, 7),
            $"期望堵住白方四连，实际选择 {move}");
    }

    [Fact]
    public void EmptyBoard_Starts_At_Center()
    {
        var board = new Board();
        var engine = new GreedyEngine();
        Assert.Equal(new Position(Board.Size / 2, Board.Size / 2), engine.SelectMove(board, Stone.Black));
    }

    [Fact]
    public void Difficulty_Has_Nine_Levels_From_Easy_To_Hard()
    {
        AIDifficulty[] levels = Enum.GetValues<AIDifficulty>();
        Assert.Equal(9, levels.Length);
        Assert.Equal(AIDifficulty.Rookie, levels[0]);      // 最易：小白
        Assert.Equal(AIDifficulty.Peak, levels[^1]);       // 最难：巅峰
    }

    [Fact]
    public void Factory_Maps_Each_Difficulty_To_Engine()
    {
        Assert.IsType<SimpleEngine>(AiEngineFactory.Create(AIDifficulty.Rookie));
        Assert.IsType<SimpleEngine>(AiEngineFactory.Create(AIDifficulty.Novice));
        Assert.IsType<SimpleEngine>(AiEngineFactory.Create(AIDifficulty.Beginner));
        Assert.IsType<GreedyEngine>(AiEngineFactory.Create(AIDifficulty.Entry));
        Assert.IsType<AlphaBetaEngine>(AiEngineFactory.Create(AIDifficulty.Normal));
        Assert.IsType<AlphaBetaEngine>(AiEngineFactory.Create(AIDifficulty.Expert));
        Assert.IsType<AlphaBetaEngine>(AiEngineFactory.Create(AIDifficulty.Master));
        Assert.IsType<AlphaBetaEngine>(AiEngineFactory.Create(AIDifficulty.Grandmaster));
        Assert.IsType<AlphaBetaEngine>(AiEngineFactory.Create(AIDifficulty.Peak));
    }

    [Fact]
    public void Rookie_Is_Weakest_But_Still_Plays_Legal_Move()
    {
        var board = new Board();
        board.PlaceStone(new Position(7, 7), Stone.Black);

        var engine = AiEngineFactory.Create(AIDifficulty.Rookie);
        Position move = engine.SelectMove(board, Stone.White);

        Assert.True(move.IsValid(Board.Size));
        Assert.True(board.IsEmpty(move));
    }
}
