using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    // 第三阶段（攻城夺权）玩家的技能。
    // - 常驻技能（ChessGame.UseSkill）：象的光束、马的火龙（DragonKnightRule）、雷暴大招。放出去就结束这一回合。
    // - 卡牌（每隔几回合二选一，拿到手里，随时用，不占回合）：下雨、树（TreePawnsRule）、山崩、自爆。
    // 潮湿 + 火（龙火、掉进岩浆）= 爆炸，见 RuleContext.Explode。

    /// <summary>
    /// 象的光束：选一个象和四条斜线之一，一直打到棋盘边（或城堡前面），线上所有棋子都被消灭，敌我都打。
    /// 象自己原地不动。光束不是火，不会引爆潮湿的棋子；打不进城堡。冻住的象放不了。
    /// </summary>
    public sealed class BishopBeamSkill : RuleBase
    {
        static readonly Coord[] Diagonals = { new Coord(1, 1), new Coord(-1, 1), new Coord(1, -1), new Coord(-1, -1) };

        public override string Id => "bishop_beam";
        public override string Title => "Bishop Beam";
        public override string Description => "Pick one of your bishops and a diagonal. Everything on that line is wiped out, yours too. The bishop stays put. Takes your turn.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Bishops(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var bishops = Bishops(ctx);
            if (bishops.Count == 0) yield break;
            var bishop = bishops[0];
            if (bishops.Count > 1)
            {
                var pickBishop = new TargetRequest
                {
                    Prompt = "Pick a bishop to fire the beam, or skip.",
                    Options = bishops.ConvertAll(b => b.Pos),
                    AllowSkip = true,
                };
                yield return pickBishop;
                if (!pickBishop.Result.HasValue) yield break;
                bishop = ctx.Board.PieceAt(pickBishop.Result.Value);
            }

            var options = new List<Coord>();
            foreach (var dir in Diagonals) options.AddRange(Path(ctx.Board, bishop.Pos, dir));
            var pickLine = new TargetRequest
            {
                Prompt = "Click a square on a diagonal to fire (it hits your pieces too). Click elsewhere to cancel.",
                Options = options,
                AllowSkip = true,
                Affected = c => LineThrough(ctx.Board, bishop.Pos, c),
                Origin = bishop.Pos,
            };
            yield return pickLine;
            if (!pickLine.Result.HasValue) yield break;

            var line = LineThrough(ctx.Board, bishop.Pos, pickLine.Result.Value);
            ctx.Acted = true;
            ctx.Emit(GameEvent.Beam(bishop, bishop.Pos, line[line.Count - 1]));
            int hit = 0;
            foreach (var c in line)
            {
                var piece = ctx.Board.PieceAt(c);
                if (piece == null) continue;
                ctx.Kill(piece, "beam", bishop);
                hit++;
            }
            ctx.Say(Speakers.Friend, hit == 0 ? "You missed. Everything." : "Bishops shoot lasers now?!");
        }

        static List<Piece> Bishops(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var b in ctx.Visible(ctx.Player, PieceType.Bishop))
            {
                if (b.IsFrozen) continue;
                if (ctx.Caster.HasValue && b.Id != ctx.Caster.Value) continue;
                foreach (var dir in Diagonals)
                    if (Path(ctx.Board, b.Pos, dir).Count > 0)
                    {
                        list.Add(b);
                        break;
                    }
            }
            return list;
        }

        /// <summary>从 from 沿 dir 一直到棋盘边的格子，碰到城堡就停在城堡前面。</summary>
        public static List<Coord> Path(Board board, Coord from, Coord dir)
        {
            var list = new List<Coord>();
            for (var c = from + dir; c.IsValid && !board.IsCastle(c); c = c + dir) list.Add(c);
            return list;
        }

        /// <summary>从 from 出发、经过 through 的那条斜线（through 必须在某条斜线上，否则为空）。</summary>
        public static List<Coord> LineThrough(Board board, Coord from, Coord through)
        {
            foreach (var dir in Diagonals)
            {
                var path = Path(board, from, dir);
                if (path.Contains(through)) return path;
            }
            return new List<Coord>();
        }
    }

    /// <summary>
    /// 雷暴大招：从棋盘上空的雷云劈下来，选一个 4 × 4 的范围，范围里的棋子全部消灭，自己人也一样（2026-11-19 改）；
    /// 只有躲在城堡里的国王、王后打不到。积累条满了才能放，放完清零重新攒（ChessGame 管积累条）。
    /// 选范围时的预览没跟着改，还是原来的样子。
    /// </summary>
    public sealed class ThunderSkill : RuleBase
    {
        public const int Size = 4;

        public override string Id => "thunder";
        public override string Title => "Thunderstorm";
        public override string Description => $"Lightning hits a {Size}x{Size} area. Every piece in it is gone, yours too. Only the king and queen in the castle are safe. Charge it by taking enemy pieces. Takes your turn.";
        public override RuleKind Kind => RuleKind.Debuff;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => ctx.Board.GuardCount > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var options = new List<Coord>();
            for (int i = 0; i < 64; i++) options.Add(Coord.FromIndex(i));
            var pick = new TargetRequest
            {
                Prompt = $"Pick where the lightning strikes ({Size}x{Size}), or skip.",
                Options = options,
                AllowSkip = true,
                Affected = c => Area(c),
            };
            yield return pick;
            if (!pick.Result.HasValue) yield break;

            var area = Area(pick.Result.Value);
            ctx.Acted = true;
            ctx.Say(Speakers.You, "Thunder!");
            ctx.Emit(GameEvent.Thunder(area));
            foreach (var c in area)
            {
                var piece = ctx.Board.PieceAt(c);
                if (piece != null && !ctx.Board.InCastle(piece)) ctx.Kill(piece, "thunder");
            }
            ctx.Say(Speakers.Friend, "Okay. OKAY. That one was scary.");
        }

        /// <summary>以 c 为中心附近的 4 × 4 范围（c 在范围里左下角那 2 × 2 的右上格），整块挪进棋盘里。</summary>
        public static List<Coord> Area(Coord c)
        {
            int x0 = Math.Max(0, Math.Min(8 - Size, c.X - Size / 2 + 1));
            int y0 = Math.Max(0, Math.Min(8 - Size, c.Y - Size / 2 + 1));
            var list = new List<Coord>();
            for (int y = y0; y < y0 + Size; y++)
            for (int x = x0; x < x0 + Size; x++)
                list.Add(new Coord(x, y));
            return list;
        }
    }

    /// <summary>下雨：场上所有棋子（双方，城堡里的除外）潮湿 WetTurns 个回合。潮湿的棋子遇到火就爆炸。</summary>
    public sealed class RainRule : RuleBase
    {
        public const int WetTurns = 3;

        public override string Id => "rain";
        public override string Title => "Rainy Day";
        public override string Description => $"Soaks every piece, yours too, for {WetTurns} turns. Wet + fire (dragon, lava) = boom: its neighbours get blasted away.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 2;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            ctx.Acted = true;
            ctx.Say(Speakers.You, "Let it rain.");
            var all = new List<Piece>(ctx.Board.PiecesOf(ctx.Player));
            all.AddRange(ctx.Board.PiecesOf(ctx.Opponent));
            ctx.Soak(all, WetTurns);
            ctx.Say(Speakers.Friend, "Everything's soaked. Don't bring fire anywhere near... oh.");
            yield break;
        }
    }

    /// <summary>
    /// 山崩：所有站在高地上（高度大于 0、不是城堡）的棋子往更低的相邻格滑一格，双方都滑。
    /// 从低处的棋子开始滑，先给上面的让出位置；往最低的那格滑（一样低的随机挑一格），只能滑到空格或岩浆，滑进岩浆就烧掉。
    /// </summary>
    public sealed class LandslideRule : RuleBase
    {
        public override string Id => "landslide";
        public override string Title => "Landslide";
        public override string Description => "Everything on high ground slides one square downhill, yours too. Slide into lava and it's gone.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 2;

        public override string UnavailableReason => "Nothing on high ground can slide downhill.";

        public override bool IsAvailable(RuleContext ctx)
        {
            foreach (var p in OnHighGround(ctx.Board))
                if (Downhill(ctx.Board, p).Count > 0) return true;
            return false;
        }

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            ctx.Acted = true;
            ctx.Say(Speakers.You, "Watch your step.");
            var sliders = OnHighGround(ctx.Board);
            sliders.Sort((a, b) => ctx.Board.Height(a.Pos).CompareTo(ctx.Board.Height(b.Pos)));
            foreach (var p in sliders)
            {
                if (!p.OnBoard) continue;
                var options = Downhill(ctx.Board, p);
                if (options.Count == 0) continue;
                ctx.Push(p, options[ctx.Rng.Next(options.Count)], null);
            }
            ctx.Say(Speakers.Friend, "Whoa whoa whoa!");
            yield break;
        }

        static List<Piece> OnHighGround(Board board)
        {
            var list = new List<Piece>();
            foreach (var p in board.AllPieces)
                if (p.OnBoard && !board.InCastle(p) && board.Height(p.Pos) > 0) list.Add(p);
            return list;
        }

        /// <summary>这枚棋子能滑去的格子：身边一圈里最低的、比它低的空格或岩浆（不是城堡）。</summary>
        public static List<Coord> Downhill(Board board, Piece piece)
        {
            int own = board.Height(piece.Pos);
            int best = own;
            var list = new List<Coord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var c = new Coord(piece.Pos.X + dx, piece.Pos.Y + dy);
                if (!c.IsValid || board.IsCastle(c) || !board.IsEmpty(c)) continue;
                int h = board.Height(c);
                if (h >= own) continue;
                if (h < best)
                {
                    best = h;
                    list.Clear();
                }
                if (h == best) list.Add(c);
            }
            return list;
        }
    }

    /// <summary>
    /// 自爆：选自己一枚棋子（王除外），它原地爆炸：按爆炸的规则推开身边的棋子、留下熔岩坑，自己死掉。
    /// </summary>
    public sealed class SelfDestructRule : RuleBase
    {
        public override string Id => "self_destruct";
        public override string Title => "Last Stand";
        public override string Description => $"One of your pieces (not the king) blows up. Neighbours get blasted away, a lava pit is left.";
        public override RuleKind Kind => RuleKind.Neutral;
        public override int Stage => 2;

        public override bool IsAvailable(RuleContext ctx) => Volunteers(ctx).Count > 0;

        public override IEnumerable<TargetRequest> Activate(RuleContext ctx)
        {
            var volunteers = Volunteers(ctx);
            if (volunteers.Count == 0) yield break;
            var pick = new TargetRequest
            {
                Prompt = "Pick which of your pieces blows up, or skip.",
                Options = volunteers.ConvertAll(p => p.Pos),
                AllowSkip = true,
                Affected = c => Neighbourhood(c),
            };
            yield return pick;
            if (!pick.Result.HasValue) yield break;

            var piece = ctx.Board.PieceAt(pick.Result.Value);
            var at = piece.Pos;
            ctx.Acted = true;
            ctx.Say(Speakers.You, "For the castle!");
            ctx.Kill(piece, "blast", piece);
            ctx.Explode(at);
            ctx.Say(Speakers.Friend, "You blew up your own guy?!");
        }

        static List<Piece> Volunteers(RuleContext ctx)
        {
            var list = new List<Piece>();
            foreach (var p in ctx.Board.PiecesOf(ctx.Player))
                if (p.Type != PieceType.King && !ctx.Board.InCastle(p)) list.Add(p);
            return list;
        }

        static IReadOnlyList<Coord> Neighbourhood(Coord c)
        {
            var list = new List<Coord>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var n = new Coord(c.X + dx, c.Y + dy);
                if (n.IsValid) list.Add(n);
            }
            return list;
        }
    }
}
