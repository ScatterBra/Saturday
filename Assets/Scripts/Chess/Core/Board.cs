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

        /// <summary>
        /// 蛋糕争夺：蛋糕所在的格子，没有蛋糕时为 null。蛋糕不是棋子、不属于任何一方：
        /// 这一格不算空（推、拉、复活都过不去），谁都能按吃子的走法走上去“吃掉”它；它也挡住滑行和攻击路线。
        /// </summary>
        public Coord? Cake { get; private set; }

        public Side SideToMove { get; private set; } = Side.White;
        public int FullMove { get; private set; } = 1;

        /// <summary>第三阶段（攻城夺权）已经开始。</summary>
        public bool Stage3 { get; private set; }
        /// <summary>第三阶段里攻城的一方（玩家）。另一方（朋友）的国王和王后躲在城堡里。</summary>
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
            if (squares[at.Index] != null || IsCake(at)) throw new InvalidOperationException($"{at} is occupied");
            var piece = new Piece { Id = pieces.Count, Type = type, Side = side, Pos = at, StartPos = at };
            pieces.Add(piece);
            squares[at.Index] = piece;
            return piece;
        }

        public Piece GetPiece(int id) => pieces[id];

        /// <summary>这个格子上的棋子，没有则为 null。</summary>
        public Piece PieceAt(Coord c) => squares[c.Index];

        public bool IsEmpty(Coord c) => squares[c.Index] == null && !IsCake(c);

        public bool IsCake(Coord c) => Cake.HasValue && Cake.Value == c;

        /// <summary>放下蛋糕（空格）。</summary>
        public void PlaceCake(Coord at)
        {
            if (!IsEmpty(at) || lava[at.Index] || castle[at.Index]) throw new InvalidOperationException($"Cannot place the cake on {at}");
            Cake = at;
        }

        /// <summary>蛋糕自己挪到另一个空格。</summary>
        public void MoveCake(Coord to)
        {
            if (!Cake.HasValue) throw new InvalidOperationException("There is no cake");
            if (squares[to.Index] != null || lava[to.Index] || castle[to.Index]) throw new InvalidOperationException($"Cannot move the cake onto {to}");
            Cake = to;
        }

        /// <summary>蛋糕没了（跑掉、风暴）。</summary>
        public void RemoveCake() => Cake = null;

        public bool IsLava(Coord c) => lava[c.Index];

        /// <summary>地面高度，0 是平地。只有第三阶段的高原和城堡大于 0。</summary>
        public int Height(Coord c) => height[c.Index];

        /// <summary>是不是城堡格（第三阶段朋友的国王和王后躲在里面）。</summary>
        public bool IsCastle(Coord c) => castle[c.Index];

        /// <summary>第三阶段：这枚棋子在城堡里（朋友的国王、王后）。城堡里的棋子不能动，城门开之前谁也碰不到。</summary>
        public bool InCastle(Piece piece) => Stage3 && piece.OnBoard && castle[piece.Pos.Index];

        /// <summary>第三阶段：守卫 = 朋友在城堡外面的所有棋子。</summary>
        public int GuardCount
        {
            get
            {
                int count = 0;
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side != Rescuer && !castle[p.Pos.Index]) count++;
                return count;
            }
        }

        /// <summary>第三阶段：守卫全部消灭，城门打开，玩家的棋子可以走进城堡夺权。</summary>
        public bool GateOpen => Stage3 && GuardCount == 0;

        /// <summary>c 紧挨着城堡（身边一圈有城堡格）。</summary>
        public bool NextToCastle(Coord c)
        {
            for (int i = 0; i < 64; i++)
                if (castle[i] && Coord.FromIndex(i).DistanceTo(c) <= 1) return true;
            return false;
        }

        /// <summary>爆炸能不能在 c 留下熔岩坑：空格、不是岩浆、不是城堡，也不紧挨着城堡（免得堵死城门）。</summary>
        public bool CanBecomeLavaPit(Coord c) =>
            Stage3 && c.IsValid && squares[c.Index] == null && !lava[c.Index] && !castle[c.Index] && !NextToCastle(c);

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
            piece.WetTurns = 0;
        }

        /// <summary>规则把棋盘上的棋子直接挪到另一个空格（例如被消防栓喷开）。不算走棋，不换走子方。</summary>
        public void Relocate(Piece piece, Coord to)
        {
            if (!piece.OnBoard) throw new InvalidOperationException($"{piece} is not on the board");
            if (squares[to.Index] != null || IsCake(to) || lava[to.Index] || castle[to.Index])
                throw new InvalidOperationException($"Cannot move {piece} onto {to}");
            squares[piece.Pos.Index] = null;
            piece.Pos = to;
            squares[to.Index] = piece;
        }

        /// <summary>棋子被推进、拉进、炸进或滑进岩浆，烧掉。第三阶段王也是普通棋子，一样会烧掉。</summary>
        public void BurnInLava(Piece piece, Coord at)
        {
            if (!lava[at.Index]) throw new InvalidOperationException($"{at} is not lava");
            RemovePiece(piece);
            piece.Pos = at;
        }

        /// <summary>把被吃掉的棋子放回棋盘（复活）。</summary>
        public void RestorePiece(Piece piece, Coord at)
        {
            if (piece.OnBoard) throw new InvalidOperationException($"{piece} is already on the board");
            if (squares[at.Index] != null || IsCake(at) || lava[at.Index] || castle[at.Index])
                throw new InvalidOperationException($"Cannot restore onto {at}");
            piece.OnBoard = true;
            piece.Pos = at;
            piece.FrozenTurns = 0;
            piece.FrozenSkin = null;
            piece.WetTurns = 0;
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
            Cake = null;
            Stage3 = true;
            Rescuer = rescuer;
            SideToMove = rescuer;
        }

        /// <summary>
        /// 风暴把双方在场上的棋子吹散，再随机撒 lavaCount 格岩浆。棋子的种类、外观、冻结都保留。
        /// - 朋友的国王和王后躲进城堡（按顺序占城堡格）。王后在第二阶段被吃了也照样出现：她逃进了城堡。
        ///   城堡格不够时，放不下的照常吹散。
        /// - 其他棋子随机落到棋盘上：不落在岩浆和城堡上；兵不落在第 1、8 行；
        ///   朋友的守卫落在城堡那半边；玩家的王身边一圈没有朋友的棋子。
        /// - 岩浆不贴着城堡、不贴着玩家的王；至少 2 格贴着朋友的棋子，保证有机会把守卫推进去、炸进去。
        /// 不满足就整盘重来。
        /// </summary>
        public void Scatter(Random rng, int lavaCount)
        {
            var friend = Rescuer.Opponent();
            var castleSquares = new List<Coord>();
            for (int i = 0; i < 64; i++)
                if (castle[i]) castleSquares.Add(Coord.FromIndex(i));
            // 离棋盘角落近的城堡格先放（默认布局 a8、a7、b8），剩下的那格留给夺权时走进去
            castleSquares.Sort((a, b) => (7 - a.Y + a.X).CompareTo(7 - b.Y + b.X));

            var royals = new List<Piece>();
            if (castleSquares.Count > 0)
            {
                var king = King(friend);
                if (king != null) royals.Add(king);
                Piece queen = null;
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side == friend && p.Type == PieceType.Queen && p.StartPos.Y == friend.HomeRank() &&
                        (queen == null || p.Id < queen.Id)) queen = p;
                if (queen == null)
                {
                    // 王后被吃了（升变出来的后不算）：她逃进了城堡，补一个
                    queen = AddPiece(PieceType.Queen, friend, FirstEmpty());
                    queen.StartPos = new Coord(3, friend.HomeRank());
                }
                royals.Add(queen);
                if (royals.Count > castleSquares.Count) royals.RemoveRange(castleSquares.Count, royals.Count - castleSquares.Count);
            }

            var order = new List<Piece>();
            foreach (var side in new[] { friend, Rescuer })
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side == side && p.Type != PieceType.King && !royals.Contains(p)) order.Add(p);
            foreach (var side in new[] { friend, Rescuer })
                foreach (var p in pieces)
                    if (p.OnBoard && p.Side == side && p.Type == PieceType.King && !royals.Contains(p)) order.Add(p);

            var fixedLava = (bool[])lava.Clone();
            bool castleOnTop = CastleOnTop(out bool hasCastle);

            for (int attempt = 0; attempt < 2000; attempt++)
            {
                Array.Clear(squares, 0, 64);
                Array.Copy(fixedLava, lava, 64);
                for (int k = 0; k < royals.Count; k++)
                {
                    royals[k].Pos = castleSquares[k];
                    squares[castleSquares[k].Index] = royals[k];
                }
                if (TryScatter(rng, order, friend, hasCastle, castleOnTop) && TryLava(rng, lavaCount, friend))
                    return;
            }
            throw new InvalidOperationException("Could not scatter the pieces for stage 3");
        }

        Coord FirstEmpty()
        {
            for (int i = 0; i < 64; i++)
                if (squares[i] == null && !lava[i] && !castle[i]) return Coord.FromIndex(i);
            throw new InvalidOperationException("The board is full");
        }

        bool TryScatter(Random rng, List<Piece> order, Side friend, bool hasCastle, bool castleOnTop)
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
            // 没有守卫（第二阶段就被吃光了）时不要求岩浆贴着谁
            int nearNeeded = GuardCount > 0 ? Math.Min(2, count) : 0;
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

        /// <summary>到最近城堡格的切比雪夫距离；没有城堡时为 0。</summary>
        public int DistanceToCastle(Coord c)
        {
            int best = int.MaxValue;
            for (int i = 0; i < 64; i++)
                if (castle[i]) best = Math.Min(best, Coord.FromIndex(i).DistanceTo(c));
            return best == int.MaxValue ? 0 : best;
        }

        /// <summary>
        /// 执行一步走法（不检查合法性，调用方负责只传入 MoveGenerator 生成的走法），然后交换走子方。
        /// 双方都正常吃子。第三阶段：城门打开后玩家的棋子走进城堡（或吃掉城堡里的国王、王后）= 夺权。
        /// </summary>
        public MoveResult Apply(Move move)
        {
            var mover = pieces[move.PieceId];
            var result = new MoveResult { Mover = mover, From = move.From, To = move.To };
            squares[move.From.Index] = null;

            var target = squares[move.To.Index];
            if (target != null)
            {
                RemovePiece(target);
                result.Captured = target;
            }

            if (IsCake(move.To))
            {
                Cake = null;
                result.CakeTaken = true;
            }

            squares[move.To.Index] = mover;
            mover.Pos = move.To;

            // 兵升变：自动变后
            if (mover.Type == PieceType.Pawn && move.To.Y == mover.Side.PromotionRank())
            {
                mover.Type = PieceType.Queen;
                result.Promoted = true;
            }

            if (Stage3 && castle[move.To.Index] && mover.Side == Rescuer) result.CastleTaken = true;

            EndTurn();
            return result;
        }

        /// <summary>没有合法走法时跳过这一回合。</summary>
        public void Pass() => EndTurn();

        void EndTurn()
        {
            // 冻结、潮湿在自己一方的回合结束时减一
            foreach (var p in pieces)
            {
                if (!p.OnBoard || p.Side != SideToMove) continue;
                if (p.FrozenTurns > 0)
                {
                    p.FrozenTurns--;
                    if (p.FrozenTurns == 0) p.FrozenSkin = null;
                }
                if (p.WetTurns > 0) p.WetTurns--;
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
            copy.Cake = Cake;
            foreach (var p in pieces)
            {
                var c = p.Clone();
                copy.pieces.Add(c);
                if (c.OnBoard) copy.squares[c.Pos.Index] = c;
            }
            return copy;
        }
    }
}
