namespace Gomoku.Core.Game;

/// <summary>对局状态机。</summary>
public enum GameSessionState
{
    Idle = 0,    // 空闲
    Playing = 1, // 对局中
    Finished = 2 // 已结束
}
