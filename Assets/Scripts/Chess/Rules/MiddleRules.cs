using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 中局阶段：规则开始影响对局。弱化类只作用于对手，强化类只作用于自己，目标随机。

    /// <summary>对手的兵变成树，冻结几个回合。</summary>
    public sealed class TreePawnsRule : RuleBase
    {
        public const int FreezeTurns = 3;

        public override string Id => "tree_pawns";
        public override string Title => "Pawns Grow Roots";
        public override string Description => $"Your friend's pawns turn into trees and can't move for {FreezeTurns} turns.";
        public override RuleKind Kind => RuleKind.Debuff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) => ctx.Visible(ctx.Opponent, PieceType.Pawn).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            foreach (var pawn in ctx.Visible(ctx.Opponent, PieceType.Pawn))
                ctx.Freeze(pawn, FreezeTurns, "tree");
            ctx.Say(Speakers.Friend, "Hey! My pawns are trees!");
            ctx.Say(Speakers.You, "That's the rule now.");
            yield break;
        }
    }

    /// <summary>自己的一匹马变成龙，喷火随机烧掉对手一个兵、马或象。</summary>
    public sealed class DragonKnightRule : RuleBase
    {
        public override string Id => "dragon_knight";
        public override string Title => "Here Be Dragons";
        public override string Description => "One of your knights becomes a dragon and burns a random enemy pawn, knight or bishop.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Knight).Count > 0 && Targets(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var knights = ctx.Visible(ctx.Player, PieceType.Knight);
            var dragon = knights[ctx.Rng.Next(knights.Count)];
            ctx.SetSkin(dragon, "dragon");
            ctx.Say(Speakers.You, "Dragon fire!");

            var victim = TargetPicker.Pick(Targets(ctx), TargetPicker.BurnWeight, ctx.Rng);
            if (victim != null) ctx.Destroy(victim, "burned", dragon);
            ctx.Say(Speakers.Friend, "That's so unfair. I love it.");
            yield break;
        }

        static List<Piece> Targets(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Opponent))
                if (p.IsTargetable && TargetPicker.BurnWeight(p) > 0) list.Add(p);
            return list;
        }
    }

    /// <summary>随机复活自己一个被吃掉的兵、马或象，样子像小孩用蜡笔画的。</summary>
    public sealed class CrayonRevivalRule : RuleBase
    {
        public override string Id => "crayon_revival";
        public override string Title => "Crayon Comeback";
        public override string Description => "One of your lost pawns, knights or bishops comes back, drawn in crayon.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx)
        {
            foreach (var p in Candidates(ctx))
                if (FindSquare(ctx.Board, p).HasValue) return true;
            return false;
        }

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx);
            candidates.RemoveAll(p => !FindSquare(ctx.Board, p).HasValue);
            var piece = TargetPicker.Pick(candidates, TargetPicker.ReviveWeight, ctx.Rng);
            if (piece != null)
            {
                ctx.Revive(piece, FindSquare(ctx.Board, piece).Value, "crayon");
                ctx.Say(Speakers.Friend, "Did you just draw that?");
                ctx.Say(Speakers.You, "It counts.");
            }
            yield break;
        }

        static List<Piece> Candidates(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.CapturedOf(ctx.Player))
                if (TargetPicker.ReviveWeight(p) > 0) list.Add(p);
            return list;
        }

        /// <summary>复活位置规则见 RevivalPlacement。</summary>
        public static Coord? FindSquare(Board board, Piece piece) => RevivalPlacement.FindSquare(board, piece);
    }
}
