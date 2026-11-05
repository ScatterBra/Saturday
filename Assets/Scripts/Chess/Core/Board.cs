using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 棋盘状态：棋子、地形（岩浆、高度、城堡）、轮到谁、第几回合。
    /// 纯数据，可以 Clone 给 AI 推演。一个“回合”是双方各走一步（full move），白方先走。
    /// </summary>
    public sealed class Board
    {
        readonly Piece[] squares = new Piece[64];
        readonly bool[] lava = new bool[64];
        readonly int[] height = new int[64];
        readonly bool[] castle = new bool[64];
        readonly List<Piece> pieces = new List<Piece>();

        public Side SideToMove { get; private set; } = Side.White;
        public int FullMove { get; private set; } = 1;

        /// <summary>第三阶段（城堡营救）已经开始。</summary>
        public bool Stage3 { get; private set; }
        /// <summary>第三阶段里要救公主的一方（玩家）。</summary>
        public Side Rescuer { get; private set; } = Side.White;

        /// <summary>整局出现过的所有棋子（包括被吃掉的），下标就是 Piece.Id。</summary>
        public IReadOnlyList<Piece> AllPieces => pieces;

        public static Board CreateStandard()
        {
            var board = new Board();
            PieceType[] backRank =
            {
                PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
                PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook,
            };
            foreach (var side in new[] { Side.White, Side.Black })
            {
                for (int x = 0; x < 8; x++)
                {
                    board.AddPiece(backRank[x], side, new Coord(x, side.HomeRank()));
                    board.AddPiece(PieceType.Pawn, side, new Coord(x, side.PawnRank()));
                }
            }
            return board;
        }

        public Piece AddPiece(PieceType type, Side side, Coord at)
        {
            if (squares[at.Index] != null) throw new InvalidOperationException($"{at} is occupied");
            var piece = new Piece { Id = pieces.Count, Type = type, Side = side, Pos = at, StartPos = at };
            pieces.Add(piece);
            squares[at.Index] = piece;
            return piece;
        }

        public Piece GetPiece(int id) => pieces[id];

        /// <summary>这个格子上的棋子，没有则为 null。</summary>
        public Piece PieceAt(Coord c) => squares[c.Index];

        public bool IsEmpty(Coord c) => squares[c.Index] == null;

        public bool IsLava(Coord c) => lava[c.Index];

        /// <summary>地面高度，0 是平地。只有第三阶段的高原和城堡大于 0。</summary>
        public int Height(Coord c) => height[c.Index];

        /// <summary>是不是公主的城堡格。</summary>
        public bool IsCastle(Coord c) => castle[c.Index];

        public void SetLava(Coord c)
        {
            if (squares[c.Index] != null) throw new InvalidOperationException($"Lava on occupied square {c}");
            lava[c.Index] = true;
        }

        /// <summary>这一方还在场上的棋子。</summary>
        public IEnumerable<Piece> PiecesOf(Side side)
        {
            foreach (var p in pieces)
                if (p.OnBoard && p.Side == side) yield return p;
        }

        /// <summary>这一方被吃掉（包括掉进岩浆烧掉）、可以复活的棋子。</summary>
        public IEnumerable<Piece> CapturedOf(Side side)
        {
            foreach (var p in pieces)
                if (p.Side == side && p.IsRevivable) yield return p;
        }

        public Piece King(Side side)
        {
            foreach (var p in pieces)
                if (p.OnBoard && p.Side == side && p.Type == PieceType.King) return p;
            return null;
        }

        /// <summary>把棋子从棋盘上拿走（被吃）。</summary>
        public void RemovePiece(Piece piece)
        {
            if (!piece.OnBoard) return;
            if (squares[piece.Pos.Index] == piece) squares[piece.Pos.Index] = null;
            piece.OnBoard = false;
            piece.WasLost = true;
            piece.FrozenTurns = 0;
            piece.FrozenSkin = null;
        }

        /// <summary>规则把棋盘上的棋子直接挪到另一个空格（例如被消防栓喷开）。不算走棋，不换走子方。</summary>
        public void Relocate(Piece piece, Coord to)
        {
            if (!piece.OnBoard) throw new InvalidOperationException($"{piece} is not on the board");
            if (squares[to.Index] != null || lava[to.Index] || castle[to.Index])
                throw new InvalidOperationException($"Cannot move {piece} onto {to}");
            squares[piece.Pos.Index] = null;
            piece.Pos = to;
            squares[to.Index] = piece;
        }

        /// <summary>棋子被推进、拉进或击飞进岩浆，烧掉（可以复活）。王不会进岩浆。</summary>
        public void BurnInLava(Piece piece, Coord at)
        {
            if (!lava[at.Index]) throw new InvalidOperationException($"{at} is not lava");
            if (piece.Type == PieceType.King) throw new InvalidOperationException("Kings never fall into the lava");
            RemovePiece(piece);
            piece.Pos = at;
        }

        /// <summary>把被吃掉的棋子放回棋盘（复活）。</summary>
        public void RestorePiece(Piece piece, Coord at)
        {
            if (piece.OnBoard) throw new InvalidOperationException($"{piece} is already on the board");
            if (squares[at.Index] != null || lava[at.Index] || castle[at.Index])
                throw new InvalidOperationException($"Cannot restore onto {at}");
            piece.OnBoard = true;
            piece.Pos = at;
            piece.FrozenTurns = 0;
            piece.FrozenSkin = null;
            squares[at.Index] = piece;
        }

        /// <summary>
        /// 进入第三阶段：铺上地形（高原、城堡、固定的岩浆），之后轮到 rescuer（玩家）。不动棋子：
        /// 游戏里接着调用 Scatter 把双方棋子吹散；测试里可以自己摆。
        /// </summary>
        public void BeginStage3(Stage3Layout layout, Side rescuer)
        {
            Array.Copy(layout.Lava, lava, 64);
            Array.Copy(layout.Heights, height, 64);
            Array.Copy(layout.Castle, castle, 64);
            Stage3 = true;
            Rescuer = rescuer;
            SideToMove = rescuer;
        }

        /// <summary>马离城堡至少要跳这么多步，风暴吹散后才不会一步就进城堡。</summary>
        public const int MinKnightStepsToCastle = 3;

        /// <summary>
        /// 风暴把双方在场上的棋子吹散：随机落到棋盘上，再随机撒 lavaCount 格岩浆。棋子的种类、外观、冻结都保留。
        /// 限制：
        /// - 棋子不落在岩浆和城堡上；兵不落在第 1、8 行。
        /// - 朋友的棋子落在城堡那半边棋盘；玩家的棋子哪里都行，但玩家的马离城堡至少 MinKnightStepsToCastle 步。
        /// - 双方的王身边一圈没有对方的棋子，朋友的王开局不被将军。
        /// - 岩浆不贴着城堡、不贴着玩家的王；至少 2 格贴着朋友的棋子，保证有机会把朋友推进去。
        /// 不满足就整盘重来。
        /// </summary>
        public void Scatter(Random rng, int lavaCount)
        {
            var friend = Rescuer.Opponent();
            var order = new List<Piece>();
            foreach (var side in new[] { friend, Rescuer })
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side == side && p.Type != PieceType.King) order.Add(p);
            foreach (var side in new[] { friend, Rescuer })
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side == side && p.Type == PieceType.King) order.Add(p);

            var fixedLava = (bool[])lava.Clone();
            var steps = KnightStepsToCastle();
            bool castleOnTop = CastleOnTop(out bool hasCastle);

            for (int attempt = 0; attempt < 2000; attempt++)
            {
                Array.Clear(squares, 0, 64);
                Array.Copy(fixedLava, lava, 64);
                if (TryScatter(rng, order, steps, friend, hasCastle, castleOnTop) && TryLava(rng, lavaCount, friend)
                    && !MoveGenerator.InCheck(this, friend))
                    return;
            }
            throw new InvalidOperationException("Could not scatter the pieces for stage 3");
        }

        bool TryScatter(Random rng, List<Piece> order, int[] steps, Side friend, bool hasCastle, bool castleOnTop)
        {
            // 非王棋子的顺序打乱；王放在最后，才能检查身边有没有对方的棋子
            int kings = 0;
            foreach (var p in order) if (p.Type == PieceType.King) kings++;
            Shuffle(rng, order, 0, order.Count - kings - 1);
            var options = new List<Coord>();
            foreach (var p in order)
            {
                options.Clear();
                for (int i = 0; i < 64; i++)
                {
                    var c = Coord.FromIndex(i);
                    if (squares[i] != null || lava[i] || castle[i]) continue;
                    if (p.Type == PieceType.Pawn && (c.Y == 0 || c.Y == 7)) continue;
                    if (p.Side == friend && hasCastle && (castleOnTop ? c.Y < 4 : c.Y > 3)) continue;
                    if (p.Side == Rescuer && p.Type == PieceType.Knight && steps[i] >= 0 && steps[i] < MinKnightStepsToCastle) continue;
                    if (p.Type == PieceType.King && NextToEnemy(c, p.Side)) continue;
                    options.Add(c);
                }
                if (options.Count == 0) return false;
                var at = options[rng.Next(options.Count)];
                p.Pos = at;
                squares[at.Index] = p;
            }
            return true;
        }

        bool TryLava(Random rng, int count, Side friend)
        {
            if (count <= 0) return true;
            var king = King(Rescuer);
            var near = new List<Coord>();
            var rest = new List<Coord>();
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (squares[i] != null || lava[i] || castle[i] || NextToCastle(c)) continue;
                if (king != null && c.DistanceTo(king.Pos) <= 1) continue;
                (NextTo(c, friend) ? near : rest).Add(c);
            }
            int nearNeeded = Math.Min(2, count);
            if (near.Count < nearNeeded || near.Count + rest.Count < count) return false;
            for (int n = 0; n < count; n++)
            {
                var pool = n < nearNeeded ? near : (rest.Count > 0 && (near.Count == 0 || rng.Next(near.Count + rest.Count) < rest.Count) ? rest : near);
                int k = rng.Next(pool.Count);
                lava[pool[k].Index] = true;
                pool.RemoveAt(k);
            }
            return true;
        }

        static void Shuffle<T>(Random rng, List<T> list, int first, int last)
        {
            for (int i = last; i > first; i--)
            {
                int j = first + rng.Next(i - first + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        bool NextToEnemy(Coord c, Side side) => NextTo(c, side.Opponent());

        /// <summary>c 身边一圈里有这一方的棋子。</summary>
        bool NextTo(Coord c, Side side)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Coord(c.X + dx, c.Y + dy);
                if (n.IsValid && squares[n.Index] != null && squares[n.Index].Side == side) return true;
            }
            return false;
        }

        bool NextToCastle(Coord c)
        {
            for (int i = 0; i < 64; i++)
                if (castle[i] && Coord.FromIndex(i).DistanceTo(c) <= 1) return true;
            return false;
        }

        /// <summary>城堡在棋盘上半边（第 5–8 行）。没有城堡时 hasCastle = false。</summary>
        bool CastleOnTop(out bool hasCastle)
        {
            int count = 0, sumY = 0;
            for (int i = 0; i < 64; i++)
            {
                if (!castle[i]) continue;
                count++;
                sumY += Coord.FromIndex(i).Y;
            }
            hasCastle = count > 0;
            return hasCastle && sumY * 2 >= count * 7;
        }

        /// <summary>
        /// 每个格子上的马要跳几步才能进城堡（只按 L 形跳数算，不管路上的棋子和岩浆）；没有城堡时全是 -1。
        /// </summary>
        public int[] KnightStepsToCastle()
        {
            var steps = new int[64];
            for (int i = 0; i < 64; i++) steps[i] = -1;
            var queue = new Queue<Coord>();
            for (int i = 0; i < 64; i++)
            {
                if (!castle[i]) continue;
                steps[i] = 0;
                queue.Enqueue(Coord.FromIndex(i));
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var jump in KnightJumps)
                {
                    var next = c + jump;
                    if (!next.IsValid || steps[next.Index] >= 0) continue;
                    steps[next.Index] = steps[c.Index] + 1;
                    queue.Enqueue(next);
                }
            }
            return steps;
        }

        static readonly Coord[] KnightJumps =
        {
            new Coord(1, 2), new Coord(2, 1), new Coord(2, -1), new Coord(1, -2),
            new Coord(-1, -2), new Coord(-2, -1), new Coord(-2, 1), new Coord(-1, 2),
        };

        /// <summary>
        /// 执行一步走法（不检查合法性，调用方负责只传入 MoveGenerator 生成的走法），然后交换走子方。
        /// 第三阶段：对手走到我方棋子上 = 把它击飞（可能飞进岩浆烧掉）；我方的马走进城堡 = 救出公主。
        /// </summary>
        public MoveResult Apply(Move move)
        {
            var mover = pieces[move.PieceId];
            var result = new MoveResult { Mover = mover, From = move.From, To = move.To };
            squares[move.From.Index] = null;

            var target = squares[move.To.Index];
            if (target != null)
            {
                if (Stage3 && target.Side == Rescuer && mover.Side != Rescuer)
                {
                    // 第三阶段对手不吃我方棋子，而是把它击飞
                    var landing = Knockback.FindLanding(this, target, move.From);
                    if (landing == null) throw new InvalidOperationException($"{target} cannot be knocked back");
                    squares[target.Pos.Index] = null;
                    result.KnockedBack = target;
                    result.KnockFrom = target.Pos;
                    result.KnockTo = landing.Value;
                    target.Pos = landing.Value;
                    if (lava[landing.Value.Index])
                    {
                        target.OnBoard = false;
                        target.WasLost = true;
                        target.FrozenTurns = 0;
                        target.FrozenSkin = null;
                        result.KnockedIntoLava = true;
                    }
                    else
                    {
                        squares[landing.Value.Index] = target;
                    }
                }
                else
                {
                    RemovePiece(target);
                    result.Captured = target;
                }
            }

            squares[move.To.Index] = mover;
            mover.Pos = move.To;

            // 兵升变：自动变后
            if (mover.Type == PieceType.Pawn && move.To.Y == mover.Side.PromotionRank())
            {
                mover.Type = PieceType.Queen;
                result.Promoted = true;
            }

            if (Stage3 && castle[move.To.Index] && mover.Type == PieceType.Knight) result.Rescued = true;

            EndTurn();
            return result;
        }

        /// <summary>没有合法走法时跳过这一回合。</summary>
        public void Pass() => EndTurn();

        void EndTurn()
        {
            // 冻结在自己一方的回合结束时减一
            foreach (var p in pieces)
            {
                if (p.OnBoard && p.Side == SideToMove && p.FrozenTurns > 0)
                {
                    p.FrozenTurns--;
                    if (p.FrozenTurns == 0) p.FrozenSkin = null;
                }
            }
            if (SideToMove == Side.Black) FullMove++;
            SideToMove = SideToMove.Opponent();
        }

        public Board Clone()
        {
            var copy = new Board
            {
                SideToMove = SideToMove, FullMove = FullMove, Stage3 = Stage3, Rescuer = Rescuer,
            };
            Array.Copy(lava, copy.lava, 64);
            Array.Copy(height, copy.height, 64);
            Array.Copy(castle, copy.castle, 64);
            foreach (var p in pieces)
            {
                var c = p.Clone();
                copy.pieces.Add(c);
                if (c.OnBoard) copy.squares[c.Pos.Index] = c;
            }
            return copy;
        }
    }

    /// <summary>
    /// 第三阶段的击飞：被打的棋子沿八个方向之一飞出两格；第二格落不下就落在第一格，第一格都落不下的方向不能选。
    /// 方向由对手（朋友）选：优先把棋子打进岩浆，其次打得离城堡越远越好、越低越好。
    /// 普通棋子掉进岩浆就死（可以复活）；不死的棋子不死，禁止行动一回合。
    /// 王不会被打进岩浆（第三阶段对手本来也不会攻击王）。
    /// </summary>
    public static class Knockback
    {
        static readonly Coord[] Directions =
        {
            new Coord(0, -1), new Coord(-1, -1), new Coord(1, -1), new Coord(-1, 0),
            new Coord(1, 0), new Coord(0, 1), new Coord(-1, 1), new Coord(1, 1),
        };

        public static Coord? FindLanding(Board board, Piece victim, Coord attackerFrom)
        {
            Coord? best = null;
            int bestScore = int.MinValue;
            foreach (var dir in Directions)
            {
                var landing = Landing(board, victim, dir, attackerFrom);
                if (landing == null) continue;
                int score = Score(board, victim, landing.Value);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = landing;
                }
            }
            return best;
        }

        static Coord? Landing(Board board, Piece victim, Coord dir, Coord attackerFrom)
        {
            var first = victim.Pos + dir;
            if (!Free(board, victim, first, attackerFrom)) return null;
            var second = first + dir;
            return Free(board, victim, second, attackerFrom) ? second : first;
        }

        static bool Free(Board board, Piece victim, Coord c, Coord attackerFrom)
        {
            if (!c.IsValid || board.IsCastle(c)) return false;
            if (!board.IsEmpty(c) && c != attackerFrom) return false;
            if (board.IsLava(c) && victim.Type == PieceType.King) return false;
            return true;
        }

        static int Score(Board board, Piece victim, Coord c)
        {
            int score = 0;
            // 打进岩浆最好（棋子会死）
            if (board.IsLava(c)) score += 1000;
            score += 10 * DistanceToCastle(board, c);
            score -= 3 * board.Height(c);
            return score;
        }

        /// <summary>到最近城堡格的切比雪夫距离；没有城堡时为 0。</summary>
        public static int DistanceToCastle(Board board, Coord c)
        {
            int best = int.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                var castle = Coord.FromIndex(i);
                if (board.IsCastle(castle)) best = Math.Min(best, castle.DistanceTo(c));
            }
            return best == int.MaxValue ? 0 : best;
        }
    }
}
