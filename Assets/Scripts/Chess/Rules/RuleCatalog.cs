using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 规则（技能）池。技能和规则是同一个东西，只是在不同的时机弹出：
    /// - 第 4、10 回合的固定选卡：CreatePool(0)、CreatePool(1)，每次三选一，必须选。
    /// - 被将军时、第三阶段：CreateSkillPool()，可以跳过。
    /// - 朋友按时间表放的技能：Create(id)。
    /// 以后加规则只需要在这里登记。
    /// </summary>
    public static class RuleCatalog
    {
        /// <summary>固定选卡的阶段数（第 4、10 回合各一次）。</summary>
        public const int StageCount = 2;

        /// <summary>规则书里显示的阶段名，下标是 RuleBase.Stage。</summary>
        public static readonly string[] StageNames = { "Opening", "Middle Game", "Castle Rescue" };

        public static List<RuleBase> CreatePool(int stage)
        {
            switch (stage)
            {
                case 0: return new List<RuleBase> { new FireHydrantRule(), new BishopPunsRule(), new ElephantBishopRule() };
                case 1: return new List<RuleBase> { new TreePawnsRule(), new DragonKnightRule(), new CrayonRevivalRule() };
                default: return new List<RuleBase>();
            }
        }

        /// <summary>
        /// 被将军时、第三阶段弹出的技能。现在先用已有的规则顶上，等实验后再换成专门的技能。
        /// </summary>
        public static List<RuleBase> CreateSkillPool() =>
            new List<RuleBase> { new RevivePieceRule(), new DragonKnightRule(), new TreePawnsRule() };

        public static RuleBase Create(string id)
        {
            switch (id)
            {
                case "fire_hydrant": return new FireHydrantRule();
                case "bishop_puns": return new BishopPunsRule();
                case "elephant": return new ElephantBishopRule();
                case "tree_pawns": return new TreePawnsRule();
                case "dragon_knight": return new DragonKnightRule();
                case "crayon_revival": return new CrayonRevivalRule();
                case "wizard_birds": return new WizardBirdsRule();
                case "revive_piece": return new RevivePieceRule();
                default: return null;
            }
        }
    }
}
