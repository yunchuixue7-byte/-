using Gomoku.Core.Domain.Models;

namespace Gomoku.Core.Domain.AI;

/// <summary>AI 落子引擎接口。</summary>
public interface IPlayerEngine
{
    /// <summary>根据当前棋盘计算下一步落子位置；token 用于支持思考过程中取消（如玩家认输）。</summary>
    Position SelectMove(Board board, Stone myStone, CancellationToken cancellationToken = default);
}
