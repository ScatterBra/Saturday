using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 朋友（AI）在第三阶段放的技能，以及复活放在哪里。玩家第三阶段的技能在 Stage3Skills.cs。

    /// <summary>
    /// 鸟群：砸中放技能一方的对手几个棋子（王除外），被砸中的冻结几个回合。
    /// 第三阶段由城堡里的王后按时间表放，砸的是玩家的棋子：前一回合先用 Plan 选好目标格子、标在棋盘上（ChessGame 发 SkillTelegraphed），
    /// 下一回合只砸到那时还站在这些格子上的棋子，玩家看得到、躲得开。
    /// </summary>
    public sealed class WizardBirdsRule : RuleBase
    {
        public const int MaxTargets = 4;
        public const int FreezeTurns = 2;

        public override string Id => "wizard_birds";
        public override string Title => "The Queen's Birds";
        public override string Description => $"A flock of birds messes on up to {MaxTargets} enemy pieces, which can't move for {FreezeTurns} turns. The king is spared.";
        public override RuleKind Kind => RuleKind.Debuff;
        public override int Stage => 2;

        /// <summary>预告过的目标格子；为 null 时直接随机挑目标。</summary>
        public IReadOnlyList<Coord> PlannedSquares;

        public override bool IsAvailable(RuleContext ctx) => Targets(ctx).Count > 0;

        /// <summary>预告：随机挑最多 MaxTargets 个目标，返回它们现在的格子。</summary>
        public List<Coord> Plan(RuleContext ctx)
        {
            var squares = new List<Coord>();
            foreach (var p in TargetPicker.PickMany(Targets(ctx), TargetPicker.BirdWeight, MaxTargets, ctx.Rng)) squares.Add(p.Pos);
            PlannedSquares = squares;
            return squares;
        }

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            if (ctx.CastByFriend) ctx.Say(Speakers.Friend, "Birds! Get those pieces!");
            else ctx.Say(Speakers.You, "I summon... the birds!");
            List<Piece> hit;
            if (PlannedSquares != null)
            {
                hit = new List<Piece>();
                foreach (var c in PlannedSquares)
                {
                    var p = ctx.Board.PieceAt(c);
                    if (p != null && p.Side == ctx.Opponent && p.IsTargetable) hit.Add(p);
                }
            }
            else hit = TargetPicker.PickMany(Targets(ctx), TargetPicker.BirdWeight, MaxTargets, ctx.Rng);
            foreach (var p in hit) ctx.Freeze(p, FreezeTurns, "poop");
            // 鸟群没有规则卡（朋友放的），效果靠对白说明
            if (ctx.CastByFriend)
            {
                if (hit.Count == 0) ctx.Say(Speakers.Friend, "You all moved?! Cheater.");
                else
                {
                    ctx.Say(Speakers.You, "Ew! Not fair!");
                    ctx.Say(Speakers.Friend, $"Messy pieces can't move for {FreezeTurns} turns. Rules are rules.");
                }
            }
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
