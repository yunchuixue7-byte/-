namespace Gomoku.Core.Configuration;

/// <summary>本地配置：AI 难度、棋盘皮肤、窗口与全屏状态等。</summary>
public sealed class AppSettings
{
    /// <summary>AI 难度下拉索引（对应 AIDifficulty 枚举，默认 4 = 普通）。</summary>
    public int AiDifficultyIndex { get; set; } = 4;

    /// <summary>棋盘皮肤下拉索引（0 = 木质，1 = 石制，2 = 动态视频）。旧版单一选择，仅用于迁移，已迁移后置为 -1。</summary>
    public int BoardSkinIndex { get; set; } = 2;

    /// <summary>棋盘材质索引（0 = 木质，1 = 石制，默认木质）。与背景独立。</summary>
    public int BoardMaterialIndex { get; set; }

    /// <summary>背景索引（0 = 无背景，1 = 动态四季视频，默认动态视频）。与棋盘材质独立。</summary>
    public int BackgroundIndex { get; set; } = 1;

    /// <summary>是否全屏。</summary>
    public bool IsFullScreen { get; set; }

    /// <summary>窗口左侧位置（未记录时为 null）。</summary>
    public double? WindowLeft { get; set; }

    /// <summary>窗口顶部位置（未记录时为 null）。</summary>
    public double? WindowTop { get; set; }

    /// <summary>窗口宽度（默认 1280）。</summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>窗口高度（默认 800）。</summary>
    public double WindowHeight { get; set; } = 800;
}

