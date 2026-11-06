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

    /// <summary>
    /// 复活自己一枚被吃掉的棋子（王以外都行）：玩家先在规则书的被吃区里挑棋子，再在自己那半边棋盘上挑位置，两步都能跳过。
    /// 复活后的样子：本来有外观的（消防栓、青蛙、主教变体）保持原样；有蜡笔图的（兵、马、车、后）画成蜡笔版；其他的（普通的象）是纸片版。
    /// 第 10 回合的选卡、被将军时的救急技能、第三阶段的技能都是它。
    /// </summary>
    public sealed class CrayonRevivalRule : RuleBase
    {
        /// <summary>有蜡笔图的棋子类型（和 ChessPieceArt 里 crayon 外观配了图的类型一致）。</summary>
        public static readonly PieceType[] CrayonTypes = { PieceType.Pawn, PieceType.Knight, PieceType.Rook, PieceType.Queen };

        public override string Id => "crayon_revival";
        public override string Title => "Crayon Comeback";
        public override string Description => "Bring back one of your lost pieces, wherever you like on your half of the board. Some come back drawn in crayon.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) => Candidates(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx);
            if (candidates.Count == 0) yield break;
            var pick = new TargetRequest
            {
                Prompt = "Pick a lost piece to bring back (in the rule book), or skip.",
                PieceOptions = candidates.ConvertAll(p => p.Id),
                AllowSkip = true,
            };
            yield return pick;
            if (!pick.PieceResult.HasValue) yield break;

            var piece = ctx.Board.GetPiece(pick.PieceResult.Value);
            string skin = LookAfterRevival(piece);
            var place = new TargetRequest
            {
                Prompt = $"Pick where your {piece.Type.ToString().ToLower()} comes back (your half of the board), or skip.",
                Options = RevivalPlacement.Squares(ctx.Board, piece),
                AllowSkip = true,
                PreviewPieceId = piece.Id,
                PreviewSkin = skin,
            };
            yield return place;
            if (!place.Result.HasValue) yield break;

            ctx.Revive(piece, place.Result.Value, skin);
            if (skin == "crayon")
            {
                ctx.Say(Speakers.Friend, "Did you just draw that?");
                ctx.Say(Speakers.You, "It counts.");
            }
            else ctx.Say(Speakers.Friend, "Welcome back, I guess.");
        }

        /// <summary>复活后的外观：有外观就保留，没有外观的、有蜡笔图的变成蜡笔版，其他的为 null（表现层显示纸片版）。</summary>
        public static string LookAfterRevival(Piece piece)
        {
            if (!string.IsNullOrEmpty(piece.Skin)) return piece.Skin;
            return System.Array.IndexOf(CrayonTypes, piece.Type) >= 0 ? "crayon" : null;
        }

        static List<Piece> Candidates(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.CapturedOf(ctx.Player))
                if (p.Type != PieceType.King && RevivalPlacement.Squares(ctx.Board, p).Count > 0) list.Add(p);
            return list;
        }
    }
}
