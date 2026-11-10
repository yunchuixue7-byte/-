namespace Gomoku.Core.Game;

/// <summary>落子结果。</summary>
public enum MoveResult
{
    /// <summary>落子成功，对局继续。</summary>
    Ok = 0,

    /// <summary>位置非法或已有棋子。</summary>
    InvalidMove = 1,

    /// <summary>本手形成五连，落子方获胜。</summary>
    Win = 2,

    /// <summary>棋盘下满且无五连，平局。</summary>
    Draw = 3,

    /// <summary>对局未在进行中，无法落子。</summary>
    NotPlaying = 4,

    /// <summary>当前窗口棋盘已满且无五连：等待玩家选择「和棋」或「扩展棋盘继续」。</summary>
    BoardFull = 5
}
