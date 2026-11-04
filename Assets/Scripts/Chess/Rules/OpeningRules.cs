using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 第一阶段（开局）：不弹卡，规则按回合自动生效（见 GameSettings.OpeningRules）。双方的棋子都会变。

    /// <summary>
    /// 车变成消防栓和青蛙：玩家 a 列的车（按开局位置算）变消防栓、h 列的变青蛙，朋友的反过来（a 列青蛙、h 列消防栓）。
    /// 消防栓每次落地，把紧挨着它的一枚棋子（不分敌友，王也算）喷开，最多两格（HydrantSpray）。
    /// 青蛙每次落地，用舌头把直线上最近的一枚敌方棋子（王也算）拉近一格（FrogTongue）。
    /// 走法都和车一样。第三阶段两种都先不发动。
    /// Id 沿用 fire_hydrant，场景里的回合表不用改。
    /// </summary>
    public sealed class FireHydrantRule : RuleBase
    {
        public const string Hydrant = "hydrant";
        public const string Frog = "frog";

        public override string Id => "fire_hydrant";
        public override string Title => "Hydrants and Frogs";
        public override string Description =>
            "Your a-rook is a fire hydrant and your h-rook is a frog. Your friend's are the other way round. " +
            "A hydrant that lands sprays the closest piece next to it up to 2 squares away, friend or foe. " +
            "A frog that lands pulls the closest enemy in a straight line, up to 3 squares away, one step closer.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Rook).Count + ctx.Visible(ctx.Opponent, PieceType.Rook).Count > 0;

        /// <summary>这个车变成什么：按开局所在的半边（a–d 算 a 列那边）和阵营决定。</summary>
        public static string SkinFor(Piece rook, Side player)
        {
            bool aSide = rook.StartPos.X < 4;
            return aSide == (rook.Side == player) ? Hydrant : Frog;
        }

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            foreach (var side in new[] { ctx.Player, ctx.Opponent })
                foreach (var rook in ctx.Visible(side, PieceType.Rook))
                    ctx.SetSkin(rook, SkinFor(rook, ctx.Player));
            ctx.Say(Speakers.Friend, "Rooks got jobs. Hydrants spray, frogs grab. Don't stand too close to either.");
            yield break;
        }

        public override void OnPieceMoved(RuleContext ctx, Piece mover, Coord from)
        {
            // 第三阶段有地形和击飞，消防栓和青蛙先不发动
            if (mover.Type != PieceType.Rook || !mover.OnBoard || ctx.Board.Stage3) return;
            if (mover.Skin == Hydrant)
            {
                var spray = HydrantSpray.Plan(ctx.Board, mover.Pos, ctx.Rng);
                if (spray.HasValue) ctx.Push(spray.Value.Piece, spray.Value.To, mover);
            }
            else if (mover.Skin == Frog)
            {
                var pull = FrogTongue.Plan(ctx.Board, mover, ctx.Rng);
                if (pull.HasValue) ctx.Push(pull.Value.Piece, pull.Value.To, mover);
            }
        }
    }

    /// <summary>
    /// 青蛙的舌头：沿八个方向看出去，每个方向上碰到的第一枚棋子如果是敌方的、离青蛙 2–MaxReach 格，就能把它往青蛙这边拉一格。
    /// 最近的优先；同样近的有好几枚时随机挑一枚。紧挨着的拉不动，而且会挡住这个方向。
    /// </summary>
    public static class FrogTongue
    {
        public const int MaxReach = 3;

        static readonly Coord[] Directions =
        {
            new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1),
            new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1),
        };

        public static HydrantSpray.Result? Plan(Board board, Piece frog, Random rng)
        {
            for (int distance = 2; distance <= MaxReach; distance++)
            {
                var options = new List<HydrantSpray.Result>();
                foreach (var dir in Directions)
                {
                    var target = FirstPiece(board, frog.Pos, dir, out int at);
                    if (target == null || at != distance || target.Side == frog.Side) continue;
                    var to = target.Pos - dir;
                    if (board.IsLava(to) || board.IsCastle(to)) continue;
                    options.Add(new HydrantSpray.Result { Piece = target, To = to });
                }
                if (options.Count > 0) return options[rng.Next(options.Count)];
            }
            return null;
        }

        /// <summary>从 from 沿 dir 看出去 MaxReach 格以内碰到的第一枚棋子，distance 是它离 from 几格。</summary>
        static Piece FirstPiece(Board board, Coord from, Coord dir, out int distance)
        {
            var next = from;
            for (distance = 1; distance <= MaxReach; distance++)
            {
                next = next + dir;
                if (!next.IsValid) break;
                var piece = board.PieceAt(next);
                if (piece != null) return piece;
            }
            distance = 0;
            return null;
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
