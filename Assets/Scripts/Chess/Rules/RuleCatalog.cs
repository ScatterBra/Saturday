using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 每个阶段的规则池。实验版每个阶段 3 条，抽卡时先过滤掉当前局面下无效的规则。
    /// 以后加规则只需要在这里登记。
    /// </summary>
    public static class RuleCatalog
    {
        public const int StageCount = 3;

        public static readonly string[] StageNames = { "Opening", "Middle Game", "End Game" };

        public static List<RuleBase> CreatePool(int stage)
        {
            switch (stage)
            {
                case 0: return new List<RuleBase> { new FireHydrantRule(), new BishopPunsRule(), new ElephantBishopRule() };
                case 1: return new List<RuleBase> { new TreePawnsRule(), new DragonKnightRule(), new CrayonRevivalRule() };
                case 2: return new List<RuleBase> { new LavaFloorRule(), new CastleRule(), new WizardBirdsRule() };
                default: return new List<RuleBase>();
            }
        }
    }
}
