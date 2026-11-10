using Gomoku.Core.Domain.AI;
using Gomoku.Core.Domain.Models;
using Gomoku.Core.Game;
using Xunit;

namespace Gomoku.Core.Tests.Game;

/// <summary>游戏控制器单元测试：对局流程、胜负与平局状态、悔棋与重开。</summary>
public class GameControllerTests
{
    [Fact]
    public void AutoPlaceForTimeout_PlacesStoneForCurrentPlayer()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        Assert.Equal(0, controller.CurrentPlayer);

        Assert.True(controller.AutoPlaceForTimeout());

        Assert.Equal(GameSessionState.Playing, controller.State);
        Assert.Equal(1, controller.CurrentPlayer);
        int stones = 0;
        for (int r = 0; r < Board.Size; r++)
        {
            for (int c = 0; c < Board.Size; c++)
            {
                if (controller.Board.GetCell(new Position(r, c)) != Stone.Empty)
                {
                    stones++;
                }
            }
        }

        Assert.Equal(1, stones);
    }

    [Fact]
    public void AutoPlaceForTimeout_WhenFinished_NoOp()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        // 黑棋横向连五获胜
        controller.ApplyMove(new Position(7, 7));
        controller.ApplyMove(new Position(0, 0));
        controller.ApplyMove(new Position(7, 8));
        controller.ApplyMove(new Position(0, 1));
        controller.ApplyMove(new Position(7, 9));
        controller.ApplyMove(new Position(0, 2));
        controller.ApplyMove(new Position(7, 10));
        controller.ApplyMove(new Position(0, 3));
        controller.ApplyMove(new Position(7, 11));

        Assert.Equal(GameSessionState.Finished, controller.State);
        Assert.False(controller.AutoPlaceForTimeout());
    }

    [Fact]
    public void StartSession_EntersPlaying()
    {
        var controller = new GameController();
        Assert.True(controller.StartSession(GameMode.LocalTwoPlayer));
        Assert.Equal(GameSessionState.Playing, controller.State);
        Assert.Equal(GameMode.LocalTwoPlayer, controller.Mode);
        Assert.Equal(0, controller.CurrentPlayer);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void StartSession_WhilePlaying_Rejected()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        Assert.False(controller.StartSession(GameMode.LocalTwoPlayer));
    }

    [Fact]
    public void ApplyMove_BlackGoesFirst()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        var result = controller.ApplyMove(new Position(7, 7));

        Assert.Equal(MoveResult.Ok, result);
        Assert.Equal(Stone.Black, controller.Board.GetCell(new Position(7, 7)));
        Assert.Equal(1, controller.CurrentPlayer);
        Assert.Equal(GameSessionState.Playing, controller.State);
    }

    [Fact]
    public void ApplyMove_InvalidPosition_Rejected()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        controller.ApplyMove(new Position(7, 7));
        var result = controller.ApplyMove(new Position(7, 7));

        Assert.Equal(MoveResult.InvalidMove, result);
        Assert.Equal(1, controller.CurrentPlayer);
    }

    [Fact]
    public void ApplyMove_NotPlaying_Rejected()
    {
        var controller = new GameController();
        Assert.Equal(MoveResult.NotPlaying, controller.ApplyMove(new Position(7, 7)));
    }

    [Fact]
    public void FiveInRow_FinishesWithWinner()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        // 黑方横向五连（间隔一步白方落子，模拟轮流对局）
        controller.ApplyMove(new Position(7, 0));
        controller.ApplyMove(new Position(0, 0));
        controller.ApplyMove(new Position(7, 1));
        controller.ApplyMove(new Position(0, 1));
        controller.ApplyMove(new Position(7, 2));
        controller.ApplyMove(new Position(0, 2));
        controller.ApplyMove(new Position(7, 3));
        controller.ApplyMove(new Position(0, 3));
        var result = controller.ApplyMove(new Position(7, 4));

        Assert.Equal(MoveResult.Win, result);
        Assert.Equal(GameSessionState.Finished, controller.State);
        Assert.Equal(Stone.Black, controller.Winner);
    }

    [Fact]
    public void Undo_AfterWin_RestoresPlaying()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        controller.ApplyMove(new Position(7, 0));
        controller.ApplyMove(new Position(0, 0));
        controller.ApplyMove(new Position(7, 1));
        controller.ApplyMove(new Position(0, 1));
        controller.ApplyMove(new Position(7, 2));
        controller.ApplyMove(new Position(0, 2));
        controller.ApplyMove(new Position(7, 3));
        controller.ApplyMove(new Position(0, 3));
        controller.ApplyMove(new Position(7, 4));

        // 终局后允许悔棋，并恢复为对局进行中、清除胜方
        Assert.True(controller.Undo());
        Assert.Equal(GameSessionState.Playing, controller.State);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void Undo_RestoresTurnAndMove()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        controller.ApplyMove(new Position(7, 7));
        controller.ApplyMove(new Position(7, 8));

        Assert.True(controller.Undo());
        // 本地双人悔棋撤掉白棋后，轮到黑方重新走
        Assert.Equal(0, controller.CurrentPlayer);
        Assert.True(controller.Board.IsEmpty(new Position(7, 8)));
        Assert.False(controller.Board.IsEmpty(new Position(7, 7)));
    }

    [Fact]
    public void Restart_StartsFreshGame()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        controller.ApplyMove(new Position(7, 0));
        controller.ApplyMove(new Position(0, 0));
        controller.ApplyMove(new Position(7, 1));
        controller.ApplyMove(new Position(0, 1));
        controller.ApplyMove(new Position(7, 2));
        controller.ApplyMove(new Position(0, 2));
        controller.ApplyMove(new Position(7, 3));
        controller.ApplyMove(new Position(0, 3));
        controller.ApplyMove(new Position(7, 4));

        Assert.True(controller.Restart());

        Assert.Equal(GameSessionState.Playing, controller.State);
        Assert.Equal(0, controller.CurrentPlayer);
        Assert.Empty(controller.Board.History);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void Resign_LocalTwoPlayer_BlackResigns_WhiteWins()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        Assert.True(controller.Resign());
        Assert.Equal(GameSessionState.Finished, controller.State);
        Assert.Equal(Stone.White, controller.Winner);
        Assert.True(controller.IsResigned);
        Assert.Equal(0, controller.ResignedBy);
    }

    [Fact]
    public void Resign_VsAi_HumanBlackResigns_WhiteWins()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.VsAi);

        Assert.True(controller.Resign());
        Assert.Equal(GameSessionState.Finished, controller.State);
        Assert.Equal(Stone.White, controller.Winner);
        Assert.True(controller.IsResigned);
        Assert.Equal(0, controller.ResignedBy);
    }

    [Fact]
    public void Resign_WhenNotPlaying_ReturnsFalse()
    {
        var controller = new GameController();
        Assert.False(controller.Resign());
        Assert.Equal(GameSessionState.Idle, controller.State);
    }

    [Fact]
    public void Undo_AfterResign_RestoresPlayingAndClearsResign()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        controller.ApplyMove(new Position(7, 7));
        controller.Resign();

        Assert.True(controller.Undo());
        Assert.Equal(GameSessionState.Playing, controller.State);
        Assert.False(controller.IsResigned);
        Assert.Null(controller.ResignedBy);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void Restart_AfterResign_ClearsResignState()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        controller.Resign();
        Assert.True(controller.Restart());

        Assert.False(controller.IsResigned);
        Assert.Null(controller.ResignedBy);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void ApplyMove_WhenBoardFull_ReturnsBoardFullAndKeepsPlaying()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);
        Board board = controller.Board;

        // 其余格子全部填白（PlaceStone 不做胜负检测；(9,9) 落黑时四周无黑子，不会构成五连）
        for (int row = 0; row < Board.Size; row++)
        {
            for (int col = 0; col < Board.Size; col++)
            {
                if (row == 9 && col == 9)
                {
                    continue;
                }

                Assert.True(board.PlaceStone(new Position(row, col), Stone.White));
            }
        }

        // 360 次 PlaceStone 翻转后 CurrentPlayer 回到黑方；落黑不五连、窗口恰好占满 → BoardFull
        MoveResult last = controller.ApplyMove(new Position(9, 9));

        Assert.Equal(MoveResult.BoardFull, last);
        Assert.Equal(GameSessionState.Playing, controller.State); // 未判平，等待玩家选择
        Assert.True(controller.IsViewFull);
    }

    [Fact]
    public void DrawGame_FinishesAsDraw()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        controller.DrawGame();

        Assert.Equal(GameSessionState.Finished, controller.State);
        Assert.Equal(Stone.Empty, controller.Winner);
    }

    [Fact]
    public void ExtendBoard_MovesViewAndAllowsNewAreaMove()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        Assert.True(controller.ExtendBoard(3)); // 向右扩展：窗口右移半块
        Assert.Equal(Board.ExtendStep, controller.Board.ViewCol);
        Assert.Equal(GameSessionState.Playing, controller.State);

        // 原棋盘与新增半块之间可继续落子
        Assert.Equal(MoveResult.Ok, controller.ApplyMove(new Position(9, 20)));
    }

    [Fact]
    public void ExtendBoard_OutOfWorldBounds_ReturnsFalse()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.LocalTwoPlayer);

        bool ok = true;
        for (int i = 0; i < 6; i++) // 每个方向最多扩展 4 次，第 5 次起失败
        {
            ok = controller.ExtendBoard(2);
        }

        Assert.False(ok);
    }

    // ---------- 人机模式执子颜色 ----------

    [Fact]
    public void StartSession_VsAiHumanWhite_AiBlackMovesFirst()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.VsAi, 1); // 玩家执白

        Assert.Equal(1, controller.HumanColor);
        Assert.Equal(0, controller.CurrentPlayer); // 黑先 → 轮到 AI（黑）
        Assert.False(controller.CanHumanPlace);    // 玩家不能落子
    }

    [Fact]
    public void TryPerformAiMove_VsAiHumanWhite_AiPlaysBlackStone()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.VsAi, 1);
        controller.AiEngine = new SimpleEngine();

        Assert.True(controller.TryPerformAiMove(out MoveResult result));
        Assert.Equal(MoveResult.Ok, result);

        Position aiPos = controller.Board.History[^1].Position;
        Assert.Equal(Stone.Black, controller.Board.GetCell(aiPos));
    }

    [Fact]
    public void Restart_KeepsHumanColor()
    {
        var controller = new GameController();
        controller.StartSession(GameMode.VsAi, 1);
        Assert.True(controller.Restart());

        Assert.Equal(1, controller.HumanColor); // 重开沿用执白选择
    }
}
