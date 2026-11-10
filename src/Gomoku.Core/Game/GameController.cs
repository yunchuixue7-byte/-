using System.Threading;
using Gomoku.Core.Domain.AI;
using Gomoku.Core.Domain.Models;
using Gomoku.Core.Domain.Rules;

namespace Gomoku.Core.Game;

/// <summary>游戏控制器：模式路由与对局状态机的统一入口。</summary>
public sealed class GameController
{
    public GameMode Mode { get; private set; }
    public GameSessionState State { get; private set; } = GameSessionState.Idle;
    public Board Board { get; } = new Board();

    /// <summary>当前落子玩家（0=黑，1=白）。</summary>
    public int CurrentPlayer => Board.CurrentPlayer;

    /// <summary>胜方棋子；对局未结束时为 Empty，平局时也为 Empty。</summary>
    public Stone Winner { get; private set; } = Stone.Empty;

    /// <summary>获胜五连坐标；未获胜或平局时为 null。</summary>
    public List<Position>? WinningLine { get; private set; }

    /// <summary>本局是否以认输结束。</summary>
    public bool IsResigned { get; private set; }

    /// <summary>认输方玩家索引（0=黑，1=白）；非认输结束为 null。</summary>
    public int? ResignedBy { get; private set; }

    /// <summary>人机模式下玩家执子颜色（0=黑，1=白；默认执黑，AI 执白）。</summary>
    public int HumanColor { get; private set; }

    /// <summary>
    /// 当前是否允许人类玩家直接落子。
    /// 人机模式下轮到 AI（非玩家颜色）时返回 false；本地双人与联机模式返回 true
    /// （联机在 M4 前为临时本地试玩）。
    /// </summary>
    public bool CanHumanPlace
    {
        get
        {
            if (State != GameSessionState.Playing)
            {
                return false;
            }

            return Mode != GameMode.VsAi || CurrentPlayer == HumanColor;
        }
    }

    /// <summary>人机模式下使用的 AI 引擎；未设置时 AI 不自动落子（界面提示待接入）。</summary>
    public IPlayerEngine? AiEngine { get; set; }

    /// <summary>当前 AI 难度的显示名（仅人机模式用）。</summary>
    public string? DifficultyLabel { get; set; }

    /// <summary>
    /// 人机模式下执行 AI 落子（AI 执玩家所选颜色的另一色）。仅在 VsAi 模式、对局进行中、
    /// 轮到 AI 且已设置引擎时落子；成功返回 true。AI 未就绪或未到 AI 回合返回 false。
    /// </summary>
    public bool TryPerformAiMove(out MoveResult result, CancellationToken cancellationToken = default)
    {
        result = MoveResult.NotPlaying;
        if (Mode != GameMode.VsAi || State != GameSessionState.Playing
            || CurrentPlayer == HumanColor || AiEngine is null)
        {
            return false;
        }

        Stone aiStone = HumanColor == 0 ? Stone.White : Stone.Black;
        Position pos = AiEngine.SelectMove(Board, aiStone, cancellationToken);
        if (!GameRules.CanPlace(Board, pos))
        {
            result = MoveResult.InvalidMove;
            return false;
        }

        result = ApplyMove(pos);
        return result is MoveResult.Ok or MoveResult.Win or MoveResult.Draw or MoveResult.BoardFull;
    }

    /// <summary>开始一局新对局。已在对局中时返回 false；humanColor 仅在传入 0/1 时设置（重开沿用已选颜色）。</summary>
    public bool StartSession(GameMode mode, int humanColor = -1)
    {
        if (State == GameSessionState.Playing)
        {
            return false;
        }

        if (humanColor is 0 or 1)
        {
            HumanColor = humanColor;
        }

        Mode = mode;
        Board.Reset();
        State = GameSessionState.Playing;
        Winner = Stone.Empty;
        WinningLine = null;
        IsResigned = false;
        ResignedBy = null;
        return true;
    }

    /// <summary>
    /// 当前玩家在 pos 落子。人机模式下轮到 AI 时由外部先调用 AI 获取位置再进入本方法；
    /// 联机模式通过网络消息进入本方法（M3/M4 阶段补充）。
    /// </summary>
    public MoveResult ApplyMove(Position pos)
    {
        if (State != GameSessionState.Playing)
        {
            return MoveResult.NotPlaying;
        }

        if (!GameRules.CanPlace(Board, pos))
        {
            return MoveResult.InvalidMove;
        }

        Stone stone = CurrentPlayer == 0 ? Stone.Black : Stone.White;
        Board.PlaceStone(pos, stone);

        if (GameRules.IsWinningMove(Board, pos, stone))
        {
            State = GameSessionState.Finished;
            Winner = stone;
            WinningLine = GameRules.FindWinningLine(Board, pos, stone);
            return MoveResult.Win;
        }

        if (GameRules.IsBoardFull(Board))
        {
            return MoveResult.BoardFull; // 窗口占满且无胜负：等待玩家选择「和棋」或「扩展棋盘继续」
        }

        return MoveResult.Ok;
    }

    /// <summary>当前可见窗口是否已占满（无空位）。</summary>
    public bool IsViewFull => GameRules.IsBoardFull(Board);

    /// <summary>棋盘占满后选择「和棋」：以平局结束对局。</summary>
    public void DrawGame()
    {
        if (State != GameSessionState.Playing)
        {
            return;
        }

        State = GameSessionState.Finished;
        Winner = Stone.Empty;
        WinningLine = null;
    }

    /// <summary>棋盘占满后选择「继续」：向指定方向扩展棋盘（0=上 1=下 2=左 3=右）。返回是否成功。</summary>
    public bool ExtendBoard(int direction)
    {
        if (State != GameSessionState.Playing)
        {
            return false;
        }

        return Board.TryExtendView(direction);
    }

    /// <summary>
    /// 认输：当前对局中一方主动认输。本地双人/联机模式由当前回合方认输；
    /// 人机模式由玩家（其所选颜色一方）认输。返回是否成功。
    /// </summary>
    public bool Resign()
    {
        if (State != GameSessionState.Playing)
        {
            return false;
        }

        int resignedBy = Mode == GameMode.VsAi ? HumanColor : CurrentPlayer;
        int winnerIndex = 1 - resignedBy;

        State = GameSessionState.Finished;
        Winner = winnerIndex == 0 ? Stone.Black : Stone.White;
        WinningLine = null;
        IsResigned = true;
        ResignedBy = resignedBy;
        return true;
    }

    private readonly Random _rng = new();

    /// <summary>超时自动落子：为当前回合方在空位中随机落一子；满盘时判平局。返回是否已处理。</summary>
    public bool AutoPlaceForTimeout()
    {
        if (State != GameSessionState.Playing)
        {
            return false;
        }

        var empties = new List<Position>();
        for (int r = Board.ViewRow; r < Board.ViewRow + Board.Size; r++)
        {
            for (int c = Board.ViewCol; c < Board.ViewCol + Board.Size; c++)
            {
                if (Board.GetCell(r, c) == Stone.Empty)
                {
                    empties.Add(new Position(r, c));
                }
            }
        }

        if (empties.Count == 0)
        {
            // 理论不可达（占满时落子会先返回 BoardFull 弹窗）；兜底判平
            State = GameSessionState.Finished;
            Winner = Stone.Empty;
            WinningLine = null;
            return true;
        }

        MoveResult result = ApplyMove(empties[_rng.Next(empties.Count)]);
        return result is MoveResult.Ok or MoveResult.Win or MoveResult.BoardFull;
    }

    /// <summary>
    /// 悔棋：人机模式下成对撤销（AI 的白子 + 玩家的黑子），回到玩家落子前；
    /// 本地双人撤销一步。对局结束后也可悔棋，并自动恢复为对局进行中。
    /// </summary>
    public bool Undo()
    {
        if (Board.History.Count == 0)
        {
            return false;
        }

        int steps = Mode == GameMode.VsAi && Board.History.Count >= 2 ? 2 : 1;
        bool any = false;
        for (int i = 0; i < steps; i++)
        {
            if (Board.UndoLast() is not null)
            {
                any = true;
            }
            else
            {
                break;
            }
        }

        // 本地双人：撤一步后轮到对方重新走（否则刚下完获胜那方再下同样位置还是赢）
        if (any && Mode == GameMode.LocalTwoPlayer)
        {
            Board.SwapCurrentPlayer();
        }

        // 悔棋后若之前是终局，恢复为对局进行中，且清除胜方
        if (any)
        {
            State = GameSessionState.Playing;
            Winner = Stone.Empty;
            WinningLine = null;
            IsResigned = false;
            ResignedBy = null;
        }

        return any;
    }

    /// <summary>重新开始当前模式的一局（对局进行中也可重开）；沿用已选的执子颜色。</summary>
    public bool Restart()
    {
        State = GameSessionState.Idle; // 先退出对局，绕过 StartSession 的防重入检查
        return StartSession(Mode);
    }
}
