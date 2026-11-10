using Gomoku.Core.Domain.Models;

namespace Gomoku.Core.Domain.Models;

/// <summary>一次落子的记录，悔棋时出栈回退。</summary>
public sealed record MoveRecord(Position Position, Stone Stone, int PlayerIndex);
