namespace Gomoku.Core.Domain.AI;

/// <summary>AI 难度分级（由易到难，共 9 档）。</summary>
public enum AIDifficulty
{
    Rookie = 0,      // 小白：完全随机落子
    Novice = 1,      // 菜鸟：随机叠加基础评分
    Beginner = 2,    // 新手：优先取胜的高分随机
    Entry = 3,       // 入门：贪心攻防
    Normal = 4,      // 普通：浅层搜索（深度 2）
    Expert = 5,      // 高手：搜索（深度 3，窄候选）
    Master = 6,      // 专家：搜索（深度 3，宽候选）
    Grandmaster = 7, // 大师：深层搜索（深度 4，窄候选）
    Peak = 8         // 巅峰：深层搜索（深度 4，宽候选）
}
