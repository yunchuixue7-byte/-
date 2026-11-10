namespace Gomoku.Core.Game;

/// <summary>对战模式。</summary>
public enum GameMode
{
    LocalTwoPlayer = 0, // 本地双人同屏
    VsAi = 1,           // 人机对战
    Online = 2          // 在线联机
}
