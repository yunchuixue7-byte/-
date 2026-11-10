using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;
using Xunit;

namespace Gomoku.Core.Tests.Rules;

/// <summary>规则引擎单元测试：落子、悔棋、四向五连判定与满盘平局。</summary>
public class GameRulesTests
{
    [Fact]
    public void EmptyBoard_NotWinning()
    {
        var board = new Board();
        Assert.False(GameRules.IsWinningMove(board, new Position(7, 7), Stone.Black));
    }

    [Fact]
    public void FourInRow_NotWinning()
    {
        var board = new Board();
        for (int col = 0; col < 4; col++)
        {
            Assert.True(board.PlaceStone(new Position(7, col), Stone.Black));
        }

        Assert.False(GameRules.IsWinningMove(board, new Position(7, 3), Stone.Black));
    }

    [Fact]
    public void HorizontalFive_Winning()
    {
        var board = new Board();
        for (int col = 0; col < 5; col++)
        {
            Assert.True(board.PlaceStone(new Position(7, col), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(7, 4), Stone.Black));
    }

    [Fact]
    public void VerticalFive_Winning()
    {
        var board = new Board();
        for (int row = 0; row < 5; row++)
        {
            Assert.True(board.PlaceStone(new Position(row, 7), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(4, 7), Stone.Black));
    }

    [Fact]
    public void MainDiagonalFive_Winning()
    {
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(board.PlaceStone(new Position(3 + i, 3 + i), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(7, 7), Stone.Black));
    }

    [Fact]
    public void AntiDiagonalFive_Winning()
    {
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(board.PlaceStone(new Position(9 - i, 3 + i), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(5, 7), Stone.Black));
    }

    [Fact]
    public void EdgeFive_Winning()
    {
        // 贴边：第一行连续五连同样判胜
        var board = new Board();
        for (int col = 0; col < 5; col++)
        {
            Assert.True(board.PlaceStone(new Position(0, col), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(0, 4), Stone.Black));
    }

    [Fact]
    public void FiveWithGap_NotWinning()
    {
        // 中间隔开不构成连续五连
        var board = new Board();
        board.PlaceStone(new Position(7, 0), Stone.Black);
        board.PlaceStone(new Position(7, 1), Stone.Black);
        board.PlaceStone(new Position(7, 3), Stone.Black);
        board.PlaceStone(new Position(7, 4), Stone.Black);
        board.PlaceStone(new Position(7, 5), Stone.Black);

        Assert.False(GameRules.IsWinningMove(board, new Position(7, 5), Stone.Black));
    }

    [Fact]
    public void PlaceStone_RejectsOccupiedCell()
    {
        var board = new Board();
        Assert.True(board.PlaceStone(new Position(7, 7), Stone.Black));
        Assert.False(board.PlaceStone(new Position(7, 7), Stone.White));
        Assert.False(board.PlaceStone(new Position(19, 19), Stone.Black));
    }

    [Fact]
    public void PlaceStone_TogglesCurrentPlayer()
    {
        var board = new Board();
        Assert.Equal(0, board.CurrentPlayer);
        board.PlaceStone(new Position(7, 7), Stone.Black);
        Assert.Equal(1, board.CurrentPlayer);
        board.PlaceStone(new Position(7, 8), Stone.White);
        Assert.Equal(0, board.CurrentPlayer);
    }

    [Fact]
    public void UndoLast_RestoresPreviousState()
    {
        var board = new Board();
        board.PlaceStone(new Position(7, 7), Stone.Black);
        board.PlaceStone(new Position(7, 8), Stone.White);

        MoveRecord? undone = board.UndoLast();

        Assert.NotNull(undone);
        Assert.Equal(Stone.White, undone!.Stone);
        Assert.Single(board.History);
        Assert.True(board.IsEmpty(new Position(7, 8)));
        Assert.False(board.IsEmpty(new Position(7, 7)));
        Assert.Equal(1, board.CurrentPlayer);
    }

    [Fact]
    public void UndoLast_EmptyBoard_ReturnsNull()
    {
        var board = new Board();
        Assert.Null(board.UndoLast());
    }

    [Fact]
    public void Reset_ClearsBoard()
    {
        var board = new Board();
        board.PlaceStone(new Position(7, 7), Stone.Black);
        board.PlaceStone(new Position(7, 8), Stone.White);

        board.Reset();

        Assert.Empty(board.History);
        Assert.Equal(0, board.CurrentPlayer);
        Assert.True(board.IsEmpty(new Position(7, 7)));
    }

    [Fact]
    public void EmptyBoard_NotFull()
    {
        var board = new Board();
        Assert.False(GameRules.IsBoardFull(board));
    }

    [Fact]
    public void FullBoard_IsFull()
    {
        var board = new Board();
        for (int row = 0; row < Board.Size; row++)
        {
            for (int col = 0; col < Board.Size; col++)
            {
                Assert.True(board.PlaceStone(new Position(row, col), Stone.Black));
            }
        }

        Assert.True(GameRules.IsBoardFull(board));
    }

    // ---------- 棋盘扩展（占满后继续对局） ----------

    [Fact]
    public void ExtendRight_ViewMovesRight_RevealsNewHalfBoard()
    {
        var board = new Board();
        Assert.True(board.TryExtendView(3)); // 选择右方扩展

        Assert.Equal(0, board.ViewRow);
        Assert.Equal(Board.ExtendStep, board.ViewCol); // 窗口右移半块
        Assert.True(board.IsInView(new Position(9, 9)));   // 原棋盘大部分仍在窗口内
        Assert.True(board.IsInView(new Position(9, 20)));  // 新增半块可落子
        Assert.False(board.IsInView(new Position(9, 30))); // 窗口外不可落子
    }

    [Fact]
    public void ExtendBoard_PlaceStoneInNewArea()
    {
        var board = new Board();
        board.TryExtendView(3);

        Assert.True(board.PlaceStone(new Position(9, 20), Stone.Black));
        Assert.Equal(Stone.Black, board.GetCell(new Position(9, 20)));
    }

    [Fact]
    public void ExtendBoard_OutOfWorldBounds_Rejected()
    {
        var board = new Board();
        bool ok = true;
        for (int i = 0; i < 6; i++) // 每方向最多扩展 4 次，第 5 次失败
        {
            ok = board.TryExtendView(2);
        }

        Assert.False(ok);
    }

    [Fact]
    public void FullBoardAfterExtend_IsFullDetectsNewWindow()
    {
        var board = new Board();
        for (int row = 0; row < Board.Size; row++)
        {
            for (int col = 0; col < Board.Size; col++)
            {
                board.PlaceStone(new Position(row, col), Stone.Black);
            }
        }

        Assert.True(GameRules.IsBoardFull(board));

        board.TryExtendView(3); // 扩展后新增半块为空
        Assert.False(GameRules.IsBoardFull(board));
    }

    [Fact]
    public void WinningLine_WorksInExtendedNegativeCoordinates()
    {
        var board = new Board();
        board.TryExtendView(2); // 向左扩展：窗口 [-9, 9]

        // 在世界坐标 -9..-5 同一行连五（位于窗口最左侧）
        for (int c = -9; c <= -5; c++)
        {
            Assert.True(board.PlaceStone(new Position(5, c), Stone.Black));
        }

        Assert.True(GameRules.IsWinningMove(board, new Position(5, -5), Stone.Black));
        Assert.NotNull(GameRules.FindWinningLine(board, new Position(5, -5), Stone.Black));
    }
}
