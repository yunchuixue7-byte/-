namespace Gomoku.Core.Domain.Models;

/// <summary>
/// 标准 19×19 棋盘模型，持有棋盘状态、落子历史与当前玩家。
/// 支持「棋盘扩展」：棋盘占满后可将可见窗口向四个方向平移半个棋盘（9 格），
/// 露出一半新棋盘继续对局；棋子使用世界坐标存储，窗口（ViewRow/ViewCol）决定可见区域。
/// </summary>
public sealed class Board
{
    public const int Size = 19;

    /// <summary>扩展平移步长：半个棋盘（19 取整为 9 格）。</summary>
    public const int ExtendStep = 9;

    /// <summary>世界坐标数组跨度（5×Size，居中放置，支持每个方向最多扩展 4 次）。</summary>
    private const int WorldSpan = 5 * Size;

    /// <summary>世界坐标 0 对应的数组下标（世界坐标范围 [-2*Size, 3*Size-1]）。</summary>
    private const int WorldOrigin = 2 * Size;

    private readonly Stone[,] _cells = new Stone[WorldSpan, WorldSpan];
    private readonly List<MoveRecord> _history = new();

    /// <summary>当前可见窗口左上角的世界坐标（棋盘扩展后偏移；初始 0,0 即标准棋盘）。</summary>
    public int ViewRow { get; private set; }
    public int ViewCol { get; private set; }

    /// <summary>当前落子玩家（0=黑，1=白），每落一子自动轮换。</summary>
    public int CurrentPlayer { get; private set; }

    /// <summary>落子历史（悔棋用，末尾为最近一步；坐标为世界坐标）。</summary>
    public IReadOnlyList<MoveRecord> History => _history;

    private static int ToIndex(int world) => world + WorldOrigin;

    public Stone GetCell(Position pos) => GetCell(pos.Row, pos.Col);

    /// <summary>直接按行列取值；世界坐标越出数组范围时返回 Empty（不抛异常）。</summary>
    public Stone GetCell(int row, int col)
    {
        int r = ToIndex(row), c = ToIndex(col);
        return r >= 0 && r < WorldSpan && c >= 0 && c < WorldSpan ? _cells[r, c] : Stone.Empty;
    }

    public bool IsEmpty(Position pos) => IsEmpty(pos.Row, pos.Col);

    public bool IsEmpty(int row, int col) => GetCell(row, col) == Stone.Empty;

    /// <summary>位置是否在当前可见窗口内（可落子区域）。</summary>
    public bool IsInView(Position pos)
        => IsInView(pos.Row, pos.Col);

    /// <summary>行列是否在当前可见窗口内（可落子区域）。</summary>
    public bool IsInView(int row, int col)
        => row >= ViewRow && row < ViewRow + Size
           && col >= ViewCol && col < ViewCol + Size;

    /// <summary>当前可见窗口内是否已无空位（棋盘占满、等待扩展或和棋的前置条件）。</summary>
    public bool IsViewFull()
    {
        for (int row = ViewRow; row < ViewRow + Size; row++)
        {
            for (int col = ViewCol; col < ViewCol + Size; col++)
            {
                if (GetCell(row, col) == Stone.Empty)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>尝试在指定位置落子。位置不在当前窗口内或已有棋子时返回 false。</summary>
    public bool PlaceStone(Position pos, Stone stone)
    {
        if (!IsInView(pos) || !IsEmpty(pos))
        {
            return false;
        }

        _cells[ToIndex(pos.Row), ToIndex(pos.Col)] = stone;
        _history.Add(new MoveRecord(pos, stone, CurrentPlayer));
        CurrentPlayer = 1 - CurrentPlayer;
        return true;
    }

    /// <summary>
    /// 向指定方向扩展棋盘（0=上 1=下 2=左 3=右）：可见窗口向该方向平移半个棋盘，
    /// 反方向露出一半新棋盘。窗口超出世界数组边界时返回 false（每个方向最多扩展 4 次）。
    /// </summary>
    public bool TryExtendView(int direction)
    {
        int dr = 0, dc = 0;
        switch (direction)
        {
            case 0: dr = -ExtendStep; break; // 向上扩展 → 窗口上移，下方露出新棋盘
            case 1: dr = ExtendStep; break;  // 向下扩展 → 窗口下移，上方露出新棋盘
            case 2: dc = -ExtendStep; break; // 向左扩展 → 窗口左移，右侧露出新棋盘
            case 3: dc = ExtendStep; break;  // 向右扩展 → 窗口右移，左侧露出新棋盘
            default: return false;
        }

        int newRow = ViewRow + dr, newCol = ViewCol + dc;
        if (newRow < -WorldOrigin || newCol < -WorldOrigin
            || newRow + Size > WorldSpan - WorldOrigin
            || newCol + Size > WorldSpan - WorldOrigin)
        {
            return false; // 超出世界数组边界，无法继续扩展
        }

        ViewRow = newRow;
        ViewCol = newCol;
        return true;
    }

    /// <summary>悔棋：撤销最近一步落子，返回被撤销的记录；无历史时返回 null。</summary>
    public MoveRecord? UndoLast()
    {
        if (_history.Count == 0)
        {
            return null;
        }

        MoveRecord last = _history[^1];
        _cells[ToIndex(last.Position.Row), ToIndex(last.Position.Col)] = Stone.Empty;
        _history.RemoveAt(_history.Count - 1);
        CurrentPlayer = last.PlayerIndex;
        return last;
    }

    /// <summary>悔棋后切换回合：让对方重新走一步。</summary>
    public void SwapCurrentPlayer() => CurrentPlayer = 1 - CurrentPlayer;

    /// <summary>清空棋盘、重置当前玩家与窗口偏移。</summary>
    public void Reset()
    {
        Array.Clear(_cells);
        _history.Clear();
        CurrentPlayer = 0;
        ViewRow = 0;
        ViewCol = 0;
    }
}
