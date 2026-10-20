using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 第一阶段（开局）：不弹卡，规则按回合自动生效（见 GameSettings.OpeningRules）。双方的棋子都会变。

    /// <summary>
    /// 所有的车变成消防栓。消防栓每次落地，都会把紧挨着它的一枚棋子（不分敌友，王也算）喷开，最多两格。
    /// </summary>
    public sealed class FireHydrantRule : RuleBase
    {
        public override string Id => "fire_hydrant";
        public override string Title => "Rooks Are Fire Hydrants";
        public override string Description =>
            "Every rook is a fire hydrant now. Whenever it lands, it sprays the closest piece next to it up to 2 squares away. Friend or foe.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Rook).Count + ctx.Visible(ctx.Opponent, PieceType.Rook).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var rook in ctx.Visible(side, PieceType.Rook))
                    ctx.SetSkin(rook, "hydrant");
            ctx.Say(Speakers.Friend, "Rooks are fire hydrants now. And they work. Don't stand too close.");
            yield break;
        }

        public override void OnPieceMoved(RuleContext ctx, Piece mover, Coord from)
        {
            // 第三阶段有地形和击飞，消防栓先不喷
            if (mover.Type != PieceType.Rook || !mover.OnBoard || ctx.Board.Stage3) return;
            var spray = HydrantSpray.Plan(ctx.Board, mover.Pos, ctx.Rng);
            if (spray.HasValue) ctx.Push(spray.Value.Piece, spray.Value.To, mover);
        }
    }

    /// <summary>
    /// 消防栓喷水：找紧挨着消防栓的棋子，沿“消防栓 → 棋子”的方向推开，最多 MaxDistance 格；路上碰到棋子或棋盘边就停在前面。
    /// 上下左右的比斜角的近；同样近的有好几枚时随机挑一枚（一格都推不动的不算）。
    /// 和第三阶段的击飞（Knockback）是两套独立的代码，以后两边可以各改各的。
    /// </summary>
    public static class HydrantSpray
    {
        public const int MaxDistance = 2;

        static readonly Coord[] Orthogonal = { new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1) };
        static readonly Coord[] Diagonal = { new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1) };

        public struct Result
        {
            public Piece Piece;
            public Coord To;
        }

        public static Result? Plan(Board board, Coord hydrant, Random rng)
        {
            foreach (var group in new[] { Orthogonal, Diagonal })
            {
                var options = new List<Result>();
                foreach (var dir in group)
                {
                    var at = hydrant + dir;
                    if (!at.IsValid) continue;
                    var piece = board.PieceAt(at);
                    if (piece == null) continue;
                    var landing = Landing(board, at, dir);
                    if (landing.HasValue) options.Add(new Result { Piece = piece, To = landing.Value });
                }
                if (options.Count > 0) return options[rng.Next(options.Count)];
            }
            return null;
        }

        /// <summary>从 at 沿 dir 最多推 MaxDistance 格，返回能停下的最远空格；一格都推不动返回 null。</summary>
        static Coord? Landing(Board board, Coord at, Coord dir)
        {
            Coord? landing = null;
            var next = at;
            for (int i = 0; i < MaxDistance; i++)
            {
                next = next + dir;
                if (!next.IsValid || !board.IsEmpty(next) || board.IsLava(next) || board.IsCastle(next)) break;
                landing = next;
            }
            return landing;
        }
    }

    /// <summary>四个主教各自随机变成一样东西（大象、Bi's shop、Bish bird、蛋糕），互不重复，之后一直保持。</summary>
    public sealed class BishopVariantsRule : RuleBase
    {
        /// <summary>主教可能变成的样子（外观 ID）。</summary>
        public static readonly string[] Skins = { "elephant", "fish_shop", "bishbird", "cake" };

        public override string Id => "bishop_variants";
        public override string Title => "Bishops Can Be Anything";
        public override string Description => "Every bishop picked something else to be: an elephant, a fish shop, a bird or a cake. They stay that way.";
        public override RuleKind Kind => RuleKind.Cosmetic;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Bishop).Count + ctx.Visible(ctx.Opponent, PieceType.Bishop).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var skins = (string[])Skins.Clone();
            for (int i = skins.Length - 1; i > 0; i--)
            {
                int j = ctx.Rng.Next(i + 1);
                (skins[i], skins[j]) = (skins[j], skins[i]);
            }

            int next = 0;
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var bishop in ctx.Visible(side, PieceType.Bishop))
                    ctx.SetSkin(bishop, skins[next++ % skins.Length]);

            ctx.Say(Speakers.Friend, "Bishops are boring. Let them be whatever they want.");
            ctx.Say(Speakers.You, "...Is that one a cake?");
            yield break;
        }
    }
}
