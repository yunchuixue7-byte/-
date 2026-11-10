namespace Gomoku.Core.Domain.AI;

/// <summary>
/// AI 引擎工厂：按难度档位创建对应引擎实例（由易到难共 9 档）。
/// </summary>
public static class AiEngineFactory
{
    public static IPlayerEngine Create(AIDifficulty difficulty)
    {
        return difficulty switch
        {
            AIDifficulty.Rookie => new SimpleEngine(poolSize: 0),   // 小白：完全随机
            AIDifficulty.Novice => new SimpleEngine(poolSize: 5),   // 菜鸟：随机为主
            AIDifficulty.Beginner => new SimpleEngine(poolSize: 2), // 新手：高分区随机
            AIDifficulty.Entry => new GreedyEngine(),               // 入门：贪心攻防
            AIDifficulty.Normal => new AlphaBetaEngine(depth: 2, candidateLimit: 12),       // 普通：浅层搜索
            AIDifficulty.Expert => new AlphaBetaEngine(depth: 3, candidateLimit: 10),       // 高手：窄候选搜索
            AIDifficulty.Master => new AlphaBetaEngine(depth: 3, candidateLimit: 14),       // 专家：宽候选搜索
            AIDifficulty.Grandmaster => new AlphaBetaEngine(depth: 4, candidateLimit: 10),  // 大师：深层窄搜索
            AIDifficulty.Peak => new AlphaBetaEngine(depth: 4, candidateLimit: 16),         // 巅峰：深层宽搜索
            _ => new GreedyEngine()
        };
    }
}
