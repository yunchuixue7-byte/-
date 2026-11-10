namespace Gomoku.Core.Domain.Models;

/// <summary>棋盘坐标（行、列，从 0 开始）。</summary>
public readonly record struct Position(int Row, int Col)
{
    public bool IsValid(int size) => Row >= 0 && Row < size && Col >= 0 && Col < size;
}
