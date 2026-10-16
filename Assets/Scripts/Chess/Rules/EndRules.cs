using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 残局阶段：制造混乱。岩浆双方都受影响；城堡由玩家点选；鸟群随机。

    /// <summary>棋盘中间随机几个空格变成岩浆，双方都不能踩，也不能滑过去。</summary>
    public sealed class LavaFloorRule : RuleBase
    {
        public const int LavaCount = 6;

        public override string Id => "lava";
        public override string Title => "The Floor Is Lava";
        public override string Description => $"{LavaCount} squares turn into lava. Nobody can step on them or slide across them. Not you, not your friend.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Candidates(ctx.Board).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var candidates = Candidates(ctx.Board);
            var chosen = new List<Coord>();
            while (chosen.Count < LavaCount && candidates.Count > 0)
            {
                int i = ctx.Rng.Next(candidates.Count);
                chosen.Add(candidates[i]);
                candidates.RemoveAt(i);
            }
            ctx.Say(Speakers.Friend, "The floor is lava!");
            ctx.AddLava(chosen);
            ctx.Say(Speakers.You, "...I think we broke it.");
            yield break;
        }

        /// <summary>中间四行的空格，并且不贴着任何一方的王（保证王不会被岩浆封死）。</summary>
        static List<Coord> Candidates(Board board)
        {
            var kings = new List<Coord>();
            foreach (var side in new[] { Side.White, Side.Black })
            {
                var king = board.King(side);
                if (king != null) kings.Add(king.Pos);
            }

            var list = new List<Coord>();
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var c = new Coord(x, y);
                    if (!board.IsEmpty(c) || board.IsLava(c)) continue;
                    if (kings.Exists(k => k.DistanceTo(c) <= 1)) continue;
                    list.Add(c);
                }
            }
            return list;
        }
    }

    /// <summary>自己的一个车变成真正的城堡：不能被吃，可以藏一个友方棋子（王除外）。</summary>
    public sealed class CastleRule : RuleBase
    {
        public override string Id => "castle";
        public override string Title => "The Rook Is a Castle";
        public override string Description => "One of your rooks becomes a real castle. It can't be captured, and one of your pieces can hide inside it.";
        public override RuleKind Kind => RuleKind.Buff;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => ctx.Visible(ctx.Player, PieceType.Rook).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var rooks = ctx.Visible(ctx.Player, PieceType.Rook);
            var rook = rooks[0];
            if (rooks.Count > 1)
            {
                var request = new TargetRequest
                {
                    Prompt = "Choose a rook to become a castle.",
                    Options = rooks.ConvertAll(r => r.Pos),
                };
                yield return request;
                rook = ctx.Board.PieceAt(request.Result.Value);
            }

            ctx.MakeCastle(rook);
            ctx.Say(Speakers.You, "The castle is actually a castle.");

            var guests = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Player))
                if (p.IsVisible && !p.IsCastle && p.Type != PieceType.King) guests.Add(p);
            if (guests.Count > 0)
            {
                var request = new TargetRequest
                {
                    Prompt = "Choose a piece to hide inside the castle, or skip.",
                    Options = guests.ConvertAll(p => p.Pos),
                    AllowSkip = true,
                };
                yield return request;
                if (request.Result.HasValue) ctx.HideInCastle(ctx.Board.PieceAt(request.Result.Value), rook);
            }
            ctx.Say(Speakers.Friend, "Fine. Nobody can touch it.");
        }
    }

    /// <summary>巫师召唤鸟群，随机砸中对手几个棋子（王除外），被砸中的冻结几个回合。</summary>
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
            ctx.Say(Speakers.You, "I summon... the birds!");
            foreach (var p in TargetPicker.PickMany(Targets(ctx), TargetPicker.BirdWeight, MaxTargets, ctx.Rng))
                ctx.Freeze(p, FreezeTurns, "poop");
            ctx.Say(Speakers.Friend, "Ew! Ew ew ew.");
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
}
