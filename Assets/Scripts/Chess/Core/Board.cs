using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 棋盘状态：棋子、地形（岩浆、高度、城堡）、轮到谁、第几回合，以及第三阶段的任务进度。
    /// 纯数据，可以 Clone 给 AI 推演。一个“回合”是双方各走一步（full move），白方先走。
    /// </summary>
    public sealed class Board
    {
        readonly Piece[] squares = new Piece[64];
        readonly bool[] lava = new bool[64];
        readonly int[] height = new int[64];
        readonly bool[] castle = new bool[64];
        readonly bool[] playerZone = new bool[64];
        readonly List<Piece> pieces = new List<Piece>();

        public Side SideToMove { get; private set; } = Side.White;
        public int FullMove { get; private set; } = 1;

        /// <summary>第三阶段（城堡营救）已经开始。</summary>
        public bool Stage3 { get; private set; }
        /// <summary>第三阶段里要救公主的一方（玩家）。</summary>
        public Side Rescuer { get; private set; } = Side.White;
        /// <summary>已经献祭进岩浆的棋子数。</summary>
        public int Sacrifices { get; private set; }
        public int SacrificesNeeded { get; private set; }
        /// <summary>献祭任务完成：岩浆不再吞子，我方棋子可以站在岩浆上、穿过岩浆，也不再被限制在岩浆这一侧。</summary>
        public bool TaskComplete => Stage3 && Sacrifices >= SacrificesNeeded;

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

        /// <summary>献祭完成前，我方棋子只能落在这些格子（岩浆这一侧）。</summary>
        public bool InPlayerZone(Coord c) => playerZone[c.Index];

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

        /// <summary>这一方被吃掉、可以复活的棋子（不含献祭的和被风暴卷走的）。</summary>
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
        /// 进入第三阶段：清掉棋盘，铺上地形；对手原来的棋子被风暴卷走，换成阵型里的守卫；
        /// 我方棋子保留种类、数量和身上的效果，按阵型重新摆。之后轮到我方。
        /// </summary>
        public void ApplyLayout(Stage3Layout layout, Side rescuer, int sacrificesNeeded)
        {
            Array.Clear(squares, 0, squares.Length);
            Array.Copy(layout.Lava, lava, 64);
            Array.Copy(layout.Heights, height, 64);
            Array.Copy(layout.Castle, castle, 64);
            Array.Copy(layout.PlayerZone, playerZone, 64);
            Stage3 = true;
            Rescuer = rescuer;
            Sacrifices = 0;
            SacrificesNeeded = sacrificesNeeded;

            var mine = new List<Piece>();
            foreach (var p in pieces)
            {
                if (!p.OnBoard) continue;
                if (p.Side == rescuer) mine.Add(p);
                else
                {
                    p.OnBoard = false;
                    p.Retired = true;
                    p.FrozenTurns = 0;
                    p.FrozenSkin = null;
                }
            }

            foreach (var (type, at) in layout.FriendPieces) AddPiece(type, rescuer.Opponent(), at);

            // 我方：王放 K，马优先放 N，其余放 *，位置不够再放进我方一侧的任意空格
            var knightSlots = new Queue<Coord>(layout.KnightSlots);
            var otherSlots = new Queue<Coord>(layout.OtherSlots);
            mine.Sort((a, b) => Order(a.Type).CompareTo(Order(b.Type)));
            foreach (var p in mine)
            {
                Coord? at = null;
                if (p.Type == PieceType.King && layout.PlayerKing.HasValue && IsEmpty(layout.PlayerKing.Value)) at = layout.PlayerKing;
                if (at == null && p.Type == PieceType.Knight) at = Take(knightSlots);
                if (at == null) at = Take(otherSlots) ?? Take(knightSlots) ?? FreeZoneSquare() ?? NearestFreeGround(layout.PlayerKing);
                if (at == null) throw new InvalidOperationException("Stage 3 layout has no room for all of the player's pieces");
                p.Pos = at.Value;
                squares[at.Value.Index] = p;
            }
            SideToMove = rescuer;
        }

        static int Order(PieceType t)
        {
            switch (t)
            {
                case PieceType.King: return 0;
                case PieceType.Knight: return 1;
                case PieceType.Queen: return 2;
                case PieceType.Rook: return 3;
                case PieceType.Bishop: return 4;
                default: return 5;
            }
        }

        Coord? Take(Queue<Coord> slots)
        {
            while (slots.Count > 0)
            {
                var c = slots.Dequeue();
                if (IsEmpty(c)) return c;
            }
            return null;
        }

        Coord? FreeZoneSquare()
        {
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (playerZone[i] && IsEmpty(c) && !lava[i] && !castle[i]) return c;
            }
            return null;
        }

        /// <summary>我方一侧摆不下时：离我方王的位置最近的空平地（可能在岩浆对面）。</summary>
        Coord? NearestFreeGround(Coord? anchor)
        {
            Coord? best = null;
            int bestDistance = int.MaxValue;
            var center = anchor ?? new Coord(4, 0);
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!IsEmpty(c) || lava[i] || castle[i] || height[i] > 0) continue;
                int d = c.DistanceTo(center);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// 执行一步走法（不检查合法性，调用方负责只传入 MoveGenerator 生成的走法），然后交换走子方。
        /// 第三阶段：献祭完成前我方走进岩浆 = 献祭；对手走到我方棋子上 = 把它击飞；马走进城堡 = 救出公主。
        /// </summary>
        public MoveResult Apply(Move move)
        {
            var mover = pieces[move.PieceId];
            var result = new MoveResult { Mover = mover, From = move.From, To = move.To };
            squares[move.From.Index] = null;

            // 献祭：献祭完成前我方棋子走进岩浆（完成后岩浆可以站）
            if (lava[move.To.Index] && Stage3 && mover.Side == Rescuer && !TaskComplete)
            {
                mover.Pos = move.To;
                mover.OnBoard = false;
                mover.Sacrificed = true;
                mover.FrozenTurns = 0;
                mover.FrozenSkin = null;
                Sacrifices++;
                result.Sacrificed = true;
                if (Sacrifices == SacrificesNeeded) result.TaskCompleted = true;
                EndTurn();
                return result;
            }

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
                    if (lava[landing.Value.Index] && target.Undying)
                    {
                        // 不死的棋子掉进岩浆不会死，只是被烫得动不了一回合
                        squares[landing.Value.Index] = target;
                        target.FrozenTurns = Math.Max(target.FrozenTurns, 1);
                        target.FrozenSkin = "scorched";
                        result.Scorched = true;
                    }
                    else if (lava[landing.Value.Index])
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
                Sacrifices = Sacrifices, SacrificesNeeded = SacrificesNeeded,
            };
            Array.Copy(lava, copy.lava, 64);
            Array.Copy(height, copy.height, 64);
            Array.Copy(castle, copy.castle, 64);
            Array.Copy(playerZone, copy.playerZone, 64);
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
            // 打进岩浆最好（普通棋子会死）；不死的棋子打进去只能让它停一回合，没那么值
            if (board.IsLava(c)) score += victim.Undying ? 200 : 1000;
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
