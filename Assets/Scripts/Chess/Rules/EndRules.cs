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

    /// <summary>
    /// 第三阶段的献祭奖励（不是规则卡）：每献祭 Board.UndyingEvery 个棋子，玩家在棋盘上点选自己的一枚棋子（王除外）变成不死。
    /// 不死的棋子被弹进岩浆不会死，只是禁止行动一回合。
    /// </summary>
    public sealed class UndyingRule : RuleBase
    {
        public override string Id => "undying";
        public override string Title => "Undying";
        public override string Description => "One of your pieces can't die anymore. Lava only stops it for a turn.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Candidates(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx);
            if (candidates.Count == 0) yield break;
            ctx.Say(Speakers.Friend, "The lava liked that. Pick one of your pieces. It won't die anymore.");
            var request = new TargetRequest
            {
                Prompt = "Pick a piece to become undying.",
                Options = candidates.ConvertAll(p => p.Pos),
            };
            yield return request;

            var piece = ctx.Board.PieceAt(request.Result.Value);
            piece.Undying = true;
            ctx.Emit(GameEvent.AppearanceChanged(piece));
            ctx.Say(Speakers.You, "Undying army, rise!");
        }

        static List<Piece> Candidates(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Player))
                if (p.Type != PieceType.King && !p.Undying) list.Add(p);
            return list;
        }
    }

    /// <summary>复活一枚自己被吃掉的棋子，由玩家在规则书下方的被吃区里选。</summary>
    public sealed class RevivePieceRule : RuleBase
    {
        public override string Id => "revive_piece";
        public override string Title => "Revive a Piece";
        public override string Description => "Pick one of your lost pieces. It comes back next to your king.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Candidates(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx);
            var request = new TargetRequest
            {
                Prompt = "Pick a lost piece to bring back (in the rule book), or skip.",
                PieceOptions = candidates.ConvertAll(p => p.Id),
                AllowSkip = true,
            };
            yield return request;
            if (!request.PieceResult.HasValue) yield break;

            var piece = ctx.Board.GetPiece(request.PieceResult.Value);
            var square = RevivalPlacement.FindSquare(ctx.Board, piece);
            if (square.HasValue)
            {
                ctx.Revive(piece, square.Value, null);
                ctx.Say(Speakers.Friend, "Welcome back, I guess.");
            }
        }

        static List<Piece> Candidates(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.CapturedOf(ctx.Player))
                if (p.Type != PieceType.King && RevivalPlacement.FindSquare(ctx.Board, p).HasValue) list.Add(p);
            return list;
        }
    }

    /// <summary>复活的棋子放在哪里。</summary>
    public static class RevivalPlacement
    {
        /// <summary>
        /// 第一、二阶段：放回原来的起始格；被占了就放到离起始格最近的空格（同样近时离自己底线近的优先）。
        /// 兵不能放在底线和升变线上。
        /// 第三阶段：放在离自己的王最近的空平地上；献祭完成前只能放在岩浆这一侧。
        /// </summary>
        public static Coord? FindSquare(Board board, Piece piece)
        {
            Coord? best = null;
            int bestDistance = int.MaxValue, bestTie = int.MaxValue;
            var king = board.King(piece.Side);
            var anchor = board.Stage3 && king != null ? king.Pos : piece.StartPos;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!board.IsEmpty(c) || board.IsLava(c) || board.IsCastle(c)) continue;
                if (piece.Type == PieceType.Pawn && (c.Y == piece.Side.HomeRank() || c.Y == piece.Side.PromotionRank())) continue;
                if (board.Stage3)
                {
                    if (board.Height(c) > 0) continue;
                    if (piece.Side == board.Rescuer && !board.TaskComplete && !board.InPlayerZone(c)) continue;
                }
                int distance = c.DistanceTo(anchor);
                int tie = System.Math.Abs(c.Y - piece.Side.HomeRank()) * 8 + c.X;
                if (distance < bestDistance || (distance == bestDistance && tie < bestTie))
                {
                    best = c;
                    bestDistance = distance;
                    bestTie = tie;
                }
            }
            return best;
        }
    }
}
