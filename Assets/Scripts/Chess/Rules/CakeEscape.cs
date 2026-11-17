using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 蛋糕争夺的几个纯计算：蛋糕落在哪、每一轮往哪跑、没抢到的一方怎么往后退。
    /// </summary>
    public static class CakeEscape
    {
        /// <summary>
        /// 落点（不能太简单）：优先选双方都不能一步就吃到的空格；到中局后后、车、象常常把所有空格都盖住，
        /// 那就选一步能吃到它的棋子最少的空格。同样少的里挑离棋盘中心最近的那一圈，再随机一格。
        /// </summary>
        public static Coord? DropSquare(Board board, Random rng)
        {
            var pool = new List<Coord>();
            int bestReach = int.MaxValue;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!CanStand(board, c)) continue;
                int reach = Reachers(board, c);
                double d = CenterDistance(c);
                bool better = reach < bestReach || (reach == bestReach && d < bestDistance - 1e-6);
                if (better)
                {
                    bestReach = reach;
                    bestDistance = d;
                    pool.Clear();
                }
                if (reach == bestReach && Math.Abs(d - bestDistance) <= 1e-6) pool.Add(c);
            }
            return pool.Count > 0 ? pool[rng.Next(pool.Count)] : (Coord?)null;
        }

        /// <summary>离中心的距离（中间四格都是 1）。</summary>
        static double CenterDistance(Coord c) => Math.Abs(c.X - 3.5) + Math.Abs(c.Y - 3.5);

        /// <summary>蛋糕放在 c 上，双方一共有几枚棋子一步就能吃到它。</summary>
        public static int Reachers(Board board, Coord c)
        {
            var next = board.Clone();
            if (next.Cake.HasValue) next.RemoveCake();
            next.PlaceCake(c);
            var pieces = new HashSet<int>();
            foreach (var side in new[] { Side.White, Side.Black })
                foreach (var move in MoveGenerator.Generate(next, side))
                    if (move.To == c) pieces.Add(move.PieceId);
            return pieces.Count;
        }

        /// <summary>蛋糕放在 c 上，任何一方有没有一步就能吃到它的走法。</summary>
        public static bool ReachableInOneMove(Board board, Coord c) => Reachers(board, c) > 0;

        /// <summary>
        /// 蛋糕这一轮往哪跑：周围八格里的空格，离最近的棋子越远越好（同样远随机挑）；
        /// 不去会让任何一方的王被将军的格子；没地方去就待着（返回 null）。
        /// </summary>
        public static Coord? NextSquare(Board board, Random rng)
        {
            if (!board.Cake.HasValue) return null;
            var from = board.Cake.Value;
            var best = new List<Coord>();
            int bestScore = int.MinValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var to = new Coord(from.X + dx, from.Y + dy);
                    if (!to.IsValid || board.PieceAt(to) != null || board.IsLava(to) || board.IsCastle(to)) continue;
                    if (ExposesKing(board, to)) continue;
                    int score = NearestPiece(board, to);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best.Clear();
                    }
                    if (score == bestScore) best.Add(to);
                }
            return best.Count > 0 ? best[rng.Next(best.Count)] : (Coord?)null;
        }

        /// <summary>
        /// 没抢到蛋糕的一方：所有棋子往自己底线退一格。从最靠后的开始退，前面的才能跟上；
        /// 退不了（棋盘边、身后有子或蛋糕、岩浆、城堡）就不动；兵最多退到第 2 行（黑方第 7 行）。
        /// 返回真的退了的棋子和它原来的位置。
        /// </summary>
        public static List<(Piece piece, Coord from)> StepBack(Board board, Side side)
        {
            int back = -side.Forward();
            var order = new List<Piece>(board.PiecesOf(side));
            // 离自己底线近的先退
            order.Sort((a, b) => Math.Abs(a.Pos.Y - side.HomeRank()).CompareTo(Math.Abs(b.Pos.Y - side.HomeRank())));
            var moved = new List<(Piece, Coord)>();
            foreach (var piece in order)
            {
                if (board.InCastle(piece)) continue;
                var to = new Coord(piece.Pos.X, piece.Pos.Y + back);
                if (!to.IsValid || !board.IsEmpty(to) || board.IsLava(to) || board.IsCastle(to)) continue;
                if (piece.Type == PieceType.Pawn && to.Y == side.HomeRank()) continue;
                var from = piece.Pos;
                board.Relocate(piece, to);
                moved.Add((piece, from));
            }
            return moved;
        }

        static bool CanStand(Board board, Coord c) => board.IsEmpty(c) && !board.IsLava(c) && !board.IsCastle(c);

        static int NearestPiece(Board board, Coord c)
        {
            int best = 8;
            foreach (var p in board.AllPieces)
                if (p.OnBoard) best = Math.Min(best, p.Pos.DistanceTo(c));
            return best;
        }

        /// <summary>蛋糕挪到 to 以后（原来的格子空出来），有没有哪一方的王本来没被将军、现在被将军了。</summary>
        static bool ExposesKing(Board board, Coord to)
        {
            if (board.Stage3) return false;
            var next = board.Clone();
            next.MoveCake(to);
            foreach (var side in new[] { Side.White, Side.Black })
                if (!MoveGenerator.InCheck(board, side) && MoveGenerator.InCheck(next, side)) return true;
            return false;
        }
    }
}
