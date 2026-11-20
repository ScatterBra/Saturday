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
        public override string Description => $"All of your friend's pawns turn into trees. Trees can't move for {FreezeTurns} turns.";
        public override RuleKind Kind => RuleKind.Debuff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) => ctx.Visible(ctx.Opponent, PieceType.Pawn).Count > 0;
        public override string UnavailableReason => "Your friend has no pawns left to turn into trees.";

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            // 算作用掉了：第三阶段的卡没标 Acted 会被当成“选目标时取消”，又弹回这组卡
            ctx.Acted = true;
            foreach (var pawn in ctx.Visible(ctx.Opponent, PieceType.Pawn))
                ctx.Freeze(pawn, FreezeTurns, "tree");
            ctx.Say(Speakers.Friend, "Hey! My pawns are trees!");
            ctx.Say(Speakers.You, "That's the rule now.");
            yield break;
        }
    }

    /// <summary>
    /// 自己的一匹马变成龙，烧掉它身边 Reach 格以内的一个敌方兵、马或象，然后变回原来的样子。
    /// 玩家自己选：先选马（只有一匹马能喷时直接用它），再选烧谁。
    /// 第二阶段的卡选了就得用，不能取消（卡出现时可以整组 Skip）；第三阶段马的技能选目标时可以取消。
    /// 龙火是火：烧到潮湿的棋子会爆炸（RuleContext.Burn）。冻住的马喷不了火，城堡里的棋子烧不到。
    /// 第二阶段是一次性的卡；第三阶段是马的常驻技能（ChessGame.UseSkill），有冷却。
    /// </summary>
    public sealed class DragonKnightRule : RuleBase
    {
        /// <summary>龙火够得着的距离（切比雪夫距离，2 = 周围两圈）。</summary>
        public const int Reach = 2;

        public override string Id => "dragon_knight";
        public override string Title => "Here Be Dragons";
        public override string Description => $"One of your knights becomes a dragon and burns an enemy pawn, knight or bishop up to {Reach} squares away.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) => Dragons(ctx).Count > 0;
        public override string UnavailableReason => $"None of your knights has an enemy pawn, knight or bishop within {Reach} squares.";

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var knights = Dragons(ctx);
            if (knights.Count == 0) yield break;
            var dragon = knights[0];
            if (knights.Count > 1)
            {
                var pickKnight = new TargetRequest
                {
                    Prompt = "Pick a knight to turn into a dragon.",
                    Options = knights.ConvertAll(k => k.Pos),
                    AllowSkip = ctx.Board.Stage3,
                };
                yield return pickKnight;
                if (!pickKnight.Result.HasValue) yield break;
                dragon = ctx.Board.PieceAt(pickKnight.Result.Value);
            }

            var victims = Targets(ctx, dragon);
            var pickVictim = new TargetRequest
            {
                Prompt = ctx.Board.Stage3 ? "Click a red enemy to burn it. Click elsewhere to cancel." : "Pick what the dragon burns.",
                Options = victims.ConvertAll(v => v.Pos),
                AllowSkip = ctx.Board.Stage3,
                Origin = dragon.Pos,
            };
            yield return pickVictim;
            if (!pickVictim.Result.HasValue) yield break;
            var victim = ctx.Board.PieceAt(pickVictim.Result.Value);

            ctx.Acted = true;
            // 记下原来的样子（例如复活回来的蜡笔马），喷完火变回去
            var originalSkin = dragon.Skin;
            ctx.SetSkin(dragon, "dragon");
            ctx.Say(Speakers.You, victim.IsWet ? "Dragon fire! ...Wait, that one's wet." : "Dragon fire!");
            ctx.Burn(victim, dragon);
            ctx.RevertSkin(dragon, originalSkin);
        }

        /// <summary>能喷火的马：没冻住，身边够得着至少一个目标。</summary>
        static List<Piece> Dragons(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var knight in ctx.Visible(ctx.Player, PieceType.Knight))
                if (!knight.IsFrozen && (!ctx.Caster.HasValue || knight.Id == ctx.Caster.Value) && Targets(ctx, knight).Count > 0) list.Add(knight);
            return list;
        }

        /// <summary>这匹马够得着的目标：Reach 格以内、城堡外的敌方兵、马、象。</summary>
        public static List<Piece> Targets(RuleContext ctx, Piece knight)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Opponent))
                if (p.IsTargetable && TargetPicker.BurnWeight(p) > 0 && !ctx.Board.InCastle(p) && p.Pos.DistanceTo(knight.Pos) <= Reach)
                    list.Add(p);
            return list;
        }
    }

    /// <summary>
    /// 复活自己一枚被吃掉的棋子（王以外都行）：玩家先在规则书的被吃区里挑棋子，再在自己那半边棋盘上挑位置。
    /// 卡选了就得用，两步都不能取消（卡出现时可以整组 Skip），免得误点右键白白浪费一次复活。
    /// 复活后的样子：本来有外观的（消防栓、青蛙、主教变体）保持原样；其他的都是蜡笔版（Replacement Paper Pieces 里那套图，
    /// 表现层按“被吃过、没有外观”画，见 PieceArt；悬浮说明叫 Crayon …）。
    /// 第 10 回合的选卡、被将军时的救急技能里有它；第三阶段不能复活。
    /// </summary>
    public sealed class CrayonRevivalRule : RuleBase
    {
        public override string Id => "crayon_revival";
        public override string Title => "Crayon Comeback";
        public override string Description => "Pick one of your lost pieces, then pick where it comes back on your half of the board.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 1;

        public override bool IsAvailable(RuleContext ctx) => Candidates(ctx).Count > 0;
        public override string UnavailableReason => "You haven't lost anything you could bring back.";

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx);
            if (candidates.Count == 0) yield break;
            var pick = new TargetRequest
            {
                Prompt = "Pick a lost piece to bring back (in the rule book).",
                PieceOptions = candidates.ConvertAll(p => p.Id),
            };
            yield return pick;
            if (!pick.PieceResult.HasValue) yield break;

            var piece = ctx.Board.GetPiece(pick.PieceResult.Value);
            string skin = LookAfterRevival(piece);
            var place = new TargetRequest
            {
                Prompt = $"Pick where your {piece.Type.ToString().ToLower()} comes back (your half of the board).",
                Options = RevivalPlacement.Squares(ctx.Board, piece),
                PreviewPieceId = piece.Id,
                PreviewSkin = skin,
            };
            yield return place;
            if (!place.Result.HasValue) yield break;

            ctx.Revive(piece, place.Result.Value, skin);
            // 没有外观的就是画回来的蜡笔版
            if (string.IsNullOrEmpty(skin))
            {
                ctx.Say(Speakers.Friend, "Did you just draw that?");
                ctx.Say(Speakers.You, "It counts.");
            }
            else ctx.Say(Speakers.Friend, "Welcome back, I guess.");
        }

        /// <summary>复活后的外观：有外观就保留；没有外观的为 null，表现层画成蜡笔版（被吃过的棋子都用那套图）。</summary>
        public static string LookAfterRevival(Piece piece) => string.IsNullOrEmpty(piece.Skin) ? null : piece.Skin;

        static List<Piece> Candidates(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.CapturedOf(ctx.Player))
                if (p.Type != PieceType.King && RevivalPlacement.Squares(ctx.Board, p).Count > 0) list.Add(p);
            return list;
        }
    }
}
