using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 第一阶段（开局）：不弹卡，规则按回合自动生效（见 GameSettings.OpeningRules）。双方的棋子都会变。

    /// <summary>
    /// 车变成消防栓和青蛙：玩家 a 列的车（按开局位置算）变消防栓、h 列的变青蛙，朋友的反过来（a 列青蛙、h 列消防栓）。
    /// 消防栓每次落地，把紧挨着它的一枚对方棋子（王也算）喷开，最多两格（HydrantSpray）。
    /// 青蛙每次落地（这一步本身没吃子时），用舌头吃掉紧挨着它的一枚敌方棋子（周围 8 格，王除外），吃一个就撑倒，躺一回合（FrogTongue）。
    /// 走法都和车一样。第三阶段也生效，推到岩浆格上的棋子会烧掉（王也一样）；城堡里的棋子推不动、吃不到。
    /// 同样近的目标有好几个时随机挑一个；玩家走的那一步会用预览里显示的那个（RuleContext.PreferredTargetId）。
    /// Id 沿用 fire_hydrant，场景里的回合表不用改。
    /// </summary>
    public sealed class FireHydrantRule : RuleBase
    {
        public const string Hydrant = "hydrant";
        public const string Frog = "frog";

        public override string Id => "fire_hydrant";
        public override string Title => "Rooks";
        public override string Description =>
            "Your a-rook is a fire hydrant and your h-rook is a frog. Your friend's are the other way round. " +
            "A hydrant that lands sprays the closest enemy next to it up to 2 squares away. " +
            "A frog that lands eats an enemy right next to it (not the king).";
        public override string RuleBookText => "Hydrants spray enemies away. Frogs eat them.";
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
            ctx.Say(Speakers.You, "Fine. If you get to change the rules, so do I!");
            ctx.Say(Speakers.You, "Hydrants and frogs. Don't stand too close to either. Got it?");
            yield break;
        }

        /// <summary>
        /// 这枚棋子（站在现在的位置）落地时可以喷或吃的目标：最近的那一组，可能有好几个同样近的。
        /// 不是消防栓、青蛙时为空。表现层用它在玩家走之前预览结果（青蛙这一步本身吃了子时，表现层自己不显示）。
        /// 青蛙的结果：Piece = 要吃的棋子，To = 青蛙自己的位置（预览的箭头从猎物指向青蛙）。
        /// </summary>
        public static List<HydrantSpray.Result> EffectCandidates(Board board, Piece mover)
        {
            if (mover.Type != PieceType.Rook || !mover.OnBoard) return new List<HydrantSpray.Result>();
            if (mover.Skin == Hydrant) return HydrantSpray.Candidates(board, mover);
            if (mover.Skin == Frog) return FrogTongue.Candidates(board, mover);
            return new List<HydrantSpray.Result>();
        }

        public override void OnPieceMoved(RuleContext ctx, Piece mover, Coord from)
        {
            // 青蛙这一步本身吃了子，就不再用舌头
            if (mover.Skin == Frog && ctx.MoveCaptured) return;
            var options = EffectCandidates(ctx.Board, mover);
            if (options.Count == 0) return;
            var pick = options[ctx.Rng.Next(options.Count)];
            foreach (var option in options)
                if (option.Piece.Id == ctx.PreferredTargetId) pick = option;
            if (mover.Skin == Frog)
            {
                FrogTongue.Eat(ctx, mover, pick.Piece);
                // 这局第一次有青蛙吃撑倒下时说一句，之后就不说了
                if (!frogNapMentioned)
                {
                    frogNapMentioned = true;
                    if (mover.Side == ctx.Player) ctx.Say(Speakers.Friend, "Your frog ate too much. It's lying down now.");
                    else ctx.Say(Speakers.You, "Your frog's stuffed. Ha.");
                }
            }
            else ctx.Push(pick.Piece, pick.To, mover);
        }

        /// <summary>这局是不是已经有青蛙倒下过（只在第一次说话）。每局新建规则对象，所以不用手动清。</summary>
        bool frogNapMentioned;
    }

    /// <summary>
    /// 青蛙的舌头（2026-11-19 改，原来是把敌人拉近一格）：沿八个方向看出去，每个方向上碰到的第一枚棋子如果是敌方的、
    /// 离青蛙 MaxReach 格以内，就能用舌头把它吃掉（算被吃，进被吃区）。王吃不了，城堡里的吃不到。
    /// 最近的优先；同样近时优先斜向的；还一样就随机挑一枚。青蛙这一步本身吃了子就不用舌头（见 FireHydrantRule.OnPieceMoved）。
    /// 舌头吃一个就撑倒：躺 NapTurns 回合不能动（冻结外观 Stuffed），起来后照常吃。
    /// </summary>
    public static class FrogTongue
    {
        /// <summary>舌头够得着几格：1 = 只吃紧挨着的（周围 8 格）。</summary>
        public const int MaxReach = 1;
        /// <summary>
        /// 吃一个就撑倒，躺几个（自己一方的）回合。舌头是在走完、回合已经交给对方之后才吃的，
        /// 所以 1 = 对方这一回合 + 自己下一回合都躺着，自己那回合结束时起来（正好停走一轮）。
        /// </summary>
        public const int NapTurns = 1;
        /// <summary>撑倒时的冻结外观。</summary>
        public const string Stuffed = "stuffed";

        /// <summary>舌头吃掉 victim，然后撑倒躺下（第一次倒下时的对白在 FireHydrantRule 里）。</summary>
        public static void Eat(RuleContext ctx, Piece frog, Piece victim)
        {
            ctx.Kill(victim, "eaten", frog);
            ctx.Freeze(frog, NapTurns, Stuffed);
        }

        static readonly Coord[] Directions =
        {
            new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1),
            new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1),
        };

        public static HydrantSpray.Result? Plan(Board board, Piece frog, Random rng)
        {
            var options = Candidates(board, frog);
            return options.Count > 0 ? options[rng.Next(options.Count)] : (HydrantSpray.Result?)null;
        }

        /// <summary>舌头能吃的最近那一组目标（同样近时只留斜向的）。To = 青蛙自己的位置。</summary>
        public static List<HydrantSpray.Result> Candidates(Board board, Piece frog)
        {
            for (int distance = 1; distance <= MaxReach; distance++)
            {
                var straight = new List<HydrantSpray.Result>();
                var diagonal = new List<HydrantSpray.Result>();
                foreach (var dir in Directions)
                {
                    var target = FirstPiece(board, frog.Pos, dir, out int at);
                    if (target == null || at != distance || target.Side == frog.Side || target.Type == PieceType.King || board.InCastle(target)) continue;
                    var option = new HydrantSpray.Result { Piece = target, To = frog.Pos, Burns = false };
                    (dir.X != 0 && dir.Y != 0 ? diagonal : straight).Add(option);
                }
                if (diagonal.Count > 0) return diagonal;
                if (straight.Count > 0) return straight;
            }
            return new List<HydrantSpray.Result>();
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
    /// 消防栓喷水：找紧挨着消防栓的对方棋子（自己人不喷），沿“消防栓 → 棋子”的方向推开，最多 MaxDistance 格；路上碰到棋子、城堡或棋盘边就停在前面，
    /// 碰到岩浆就掉进去。城堡里的棋子喷不动。
    /// 上下左右的比斜角的近；同样近的有好几枚时随机挑一枚（一格都推不动的不算）。
    /// 和第三阶段的击飞（Knockback）是两套独立的代码，以后两边可以各改各的。
    /// </summary>
    public static class HydrantSpray
    {
        public const int MaxDistance = 2;

        static readonly Coord[] Orthogonal = { new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1) };
        static readonly Coord[] Diagonal = { new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1) };

        /// <summary>一次喷水或拉舌头：把谁弄到哪一格，会不会掉进岩浆。</summary>
        public struct Result
        {
            public Piece Piece;
            public Coord To;
            public bool Burns;
        }

        public static Result? Plan(Board board, Piece hydrant, Random rng)
        {
            var options = Candidates(board, hydrant);
            return options.Count > 0 ? options[rng.Next(options.Count)] : (Result?)null;
        }

        /// <summary>最近的那一组对方棋子（先上下左右，没有再斜角）。</summary>
        public static List<Result> Candidates(Board board, Piece hydrant)
        {
            foreach (var group in new[] { Orthogonal, Diagonal })
            {
                var options = new List<Result>();
                foreach (var dir in group)
                {
                    var at = hydrant.Pos + dir;
                    if (!at.IsValid) continue;
                    var piece = board.PieceAt(at);
                    if (piece == null || piece.Side == hydrant.Side || board.InCastle(piece)) continue;
                    var landing = Landing(board, piece, dir);
                    if (landing.HasValue) options.Add(new Result { Piece = piece, To = landing.Value, Burns = board.IsLava(landing.Value) });
                }
                if (options.Count > 0) return options;
            }
            return new List<Result>();
        }

        /// <summary>从棋子的位置沿 dir 最多推 MaxDistance 格，返回能停下的最远格子（岩浆格算，棋子掉进去）；一格都推不动返回 null。</summary>
        static Coord? Landing(Board board, Piece piece, Coord dir)
        {
            Coord? landing = null;
            var next = piece.Pos;
            for (int i = 0; i < MaxDistance; i++)
            {
                next = next + dir;
                if (!next.IsValid || !board.IsEmpty(next) || board.IsCastle(next)) break;
                if (board.IsLava(next))
                {
                    landing = next;
                    break;
                }
                landing = next;
            }
            return landing;
        }
    }

    /// <summary>
    /// 主教变成别的东西：Bi's shop 和 Bish-bird 两种随机分给双方，每一方只有一个主教变（随机挑一个），
    /// 另一个保持原样。四个主教里只有两个变体，之后一直保持。
    /// </summary>
    public sealed class BishopVariantsRule : RuleBase
    {
        /// <summary>主教可能变成的样子（外观 ID）。</summary>
        public static readonly string[] Skins = { "fish_shop", "bishbird" };

        public override string Id => "bishop_variants";
        public override string Title => "Bishops";
        public override string Description => "Every bishop picked something else to be: a fish shop or a bird. They stay that way.";
        public override string RuleBookText => "Bishops picked new looks. They still move like bishops.";
        public override RuleKind Kind => RuleKind.Cosmetic;
        public override int Stage => 0;

        public override bool IsAvailable(RuleContext ctx) =>
            ctx.Visible(ctx.Player, PieceType.Bishop).Count + ctx.Visible(ctx.Opponent, PieceType.Bishop).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            // 两种样子随机分给双方，每一方随机挑一个主教来变
            var skins = (string[])Skins.Clone();
            if (ctx.Rng.Next(2) == 0) (skins[0], skins[1]) = (skins[1], skins[0]);
            var sides = new[] { ctx.Player, ctx.Opponent };
            for (int k = 0; k < sides.Length; k++)
            {
                var bishops = ctx.Visible(sides[k], PieceType.Bishop);
                if (bishops.Count == 0) continue;
                ctx.SetSkin(bishops[ctx.Rng.Next(bishops.Count)], skins[k % skins.Length]);
            }

            ctx.Say(Speakers.Friend, "Bishops are boring. Let them be whatever they want.");
            ctx.Say(Speakers.You, "...Is that one a bird?");
            // 提示（字幕里不带说话人，停一会儿自己过去）：第一次有棋子换样子，告诉玩家鼠标停上去能看本事
            ctx.Emit(GameEvent.Notice("Hover over pieces to see their capabilities."));
            yield break;
        }
    }
}
