using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 开局阶段：只改外观，不影响对局，让玩家先熟悉“规则会变”这件事。双方的棋子都会变。

    /// <summary>所有的车变成消防栓。</summary>
    public sealed class FireHydrantRule : RuleBase
    {
        public override string Id => "fire_hydrant";
        public override string Title => "Rooks Are Fire Hydrants";
        public override string Description => "Every rook on the board is a fire hydrant now. It still moves like a rook.";
        public override RuleKind Kind => RuleKind.Cosmetic;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Rook).Count + ctx.Visible(ctx.Opponent, PieceType.Rook).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var rook in ctx.Visible(side, PieceType.Rook))
                    ctx.SetSkin(rook, "hydrant");
            ctx.Say(Speakers.Friend, "Okay. That's the rule now.");
            yield break;
        }
    }

    /// <summary>主教的谐音梗：每过一个回合换一个名字和样子。</summary>
    public sealed class BishopPunsRule : RuleBase
    {
        static readonly (string skin, string speaker, string line)[] Steps =
        {
            ("bish_hop", Speakers.You, "Bish... hop!"),
            ("fish_shop", Speakers.Friend, "Bi's shop. Fresh fish, half price."),
            ("bishbug", Speakers.You, "Bish-bug."),
            ("bishbird", Speakers.Friend, "Bish-bird!"),
            ("bishblob", Speakers.You, "...Bish-blob."),
        };

        readonly List<int> bishopIds = new List<int>();
        int step;

        public override string Id => "bishop_puns";
        public override string Title => "Bish-Hop";
        public override string Description => "The bishops can't stop changing their names. Every turn they become something new.";
        public override RuleKind Kind => RuleKind.Cosmetic;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Bishop).Count + ctx.Visible(ctx.Opponent, PieceType.Bishop).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            bishopIds.Clear();
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var bishop in ctx.Visible(side, PieceType.Bishop))
                    bishopIds.Add(bishop.Id);
            step = 0;
            ApplyStep(ctx);
            yield break;
        }

        public override void OnTurnEnded(RuleContext ctx, Side sideThatMoved)
        {
            // 每个完整回合（对手走完）换一次
            if (sideThatMoved != ctx.Opponent || step >= Steps.Length - 1) return;
            step++;
            ApplyStep(ctx);
        }

        void ApplyStep(RuleContext ctx)
        {
            var (skin, speaker, line) = Steps[step];
            foreach (var id in bishopIds)
            {
                var bishop = ctx.Board.GetPiece(id);
                // 被吃掉的、升变来的、或者已经被别的规则换了外观的就不管了
                if (bishop.IsVisible && bishop.Type == PieceType.Bishop) ctx.SetSkin(bishop, skin);
            }
            ctx.Say(speaker, line);
        }
    }

    /// <summary>主教在别的文化里其实是大象。</summary>
    public sealed class ElephantBishopRule : RuleBase
    {
        public override string Id => "elephant";
        public override string Title => "Bishops Are Elephants";
        public override string Description => "In some parts of the world the bishop is an elephant. Today, so is ours.";
        public override RuleKind Kind => RuleKind.Cosmetic;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Bishop).Count + ctx.Visible(ctx.Opponent, PieceType.Bishop).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var bishop in ctx.Visible(side, PieceType.Bishop))
                    ctx.SetSkin(bishop, "elephant");
            ctx.Say(Speakers.Friend, "In xiangqi it's an elephant, you know.");
            ctx.Say(Speakers.You, "Then it's an elephant.");
            yield break;
        }
    }
}
