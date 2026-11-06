using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 技能：被将军时的救急技能、第三阶段的技能，以及朋友（AI）放的技能。

    /// <summary>
    /// 巫师召唤鸟群，随机砸中放技能一方的对手几个棋子（王除外），被砸中的冻结几个回合。
    /// 第三阶段由朋友按时间表放，砸的是玩家的棋子。
    /// </summary>
    public sealed class WizardBirdsRule : RuleBase
    {
        public const int MaxTargets = 4;
        public const int FreezeTurns = 2;

        public override string Id => "wizard_birds";
        public override string Title => "The Wizard's Birds";
        public override string Description => $"A wizard calls a flock of birds. They mess on up to {MaxTargets} random enemy pieces, which can't move for {FreezeTurns} turns. The king is spared.";
        public override RuleKind Kind => RuleKind.Debuff;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Targets(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            if (ctx.CastByFriend) ctx.Say(Speakers.Friend, "Birds! Get those pieces!");
            else ctx.Say(Speakers.You, "I summon... the birds!");
            foreach (var p in TargetPicker.PickMany(Targets(ctx), TargetPicker.BirdWeight, MaxTargets, ctx.Rng))
                ctx.Freeze(p, FreezeTurns, "poop");
            if (ctx.CastByFriend) ctx.Say(Speakers.You, "Ew! Not fair!");
            else ctx.Say(Speakers.Friend, "Ew! Ew ew ew.");
            yield break;
        }

        static List<Piece> Targets(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Opponent))
                if (p.IsTargetable && TargetPicker.BirdWeight(p) > 0) list.Add(p);
            return list;
        }
    }

    /// <summary>复活的棋子放在哪里。</summary>
    public static class RevivalPlacement
    {
        /// <summary>
        /// 复活时可以放的格子：这一方自己那半边棋盘（白方第 1–4 行，黑方第 5–8 行）上的空格，
        /// 不能是岩浆、城堡；兵不能放在底线和升变线上。
        /// </summary>
        public static List<Coord> Squares(Board board, Piece piece)
        {
            var list = new List<Coord>();
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (piece.Side == Side.White ? c.Y > 3 : c.Y < 4) continue;
                if (!board.IsEmpty(c) || board.IsLava(c) || board.IsCastle(c)) continue;
                if (piece.Type == PieceType.Pawn && (c.Y == piece.Side.HomeRank() || c.Y == piece.Side.PromotionRank())) continue;
                list.Add(c);
            }
            return list;
        }
    }
}
