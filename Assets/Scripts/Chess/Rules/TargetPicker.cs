using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 加权随机。价值越低的棋子权重越高；权重为 0 的棋子永远不会被选中（例如王、后、车）。
    /// 这样随机效果能带来惊喜，但不会一下子决定胜负。
    /// </summary>
    public static class TargetPicker
    {
        /// <summary>龙火：只烧兵、马、象，兵最容易被烧。</summary>
        public static int BurnWeight(Piece p)
        {
            switch (p.Type)
            {
                case PieceType.Pawn: return 3;
                case PieceType.Knight:
                case PieceType.Bishop: return 1;
                default: return 0;
            }
        }

        /// <summary>鸟群：王除外，后可以被砸中但概率很低。</summary>
        public static int BirdWeight(Piece p)
        {
            switch (p.Type)
            {
                case PieceType.Pawn: return 3;
                case PieceType.Knight:
                case PieceType.Bishop:
                case PieceType.Rook: return 2;
                case PieceType.Queen: return 1;
                default: return 0;
            }
        }

        /// <summary>复活：只复活兵、马、象，不复活后和车。</summary>
        public static int ReviveWeight(Piece p)
        {
            switch (p.Type)
            {
                case PieceType.Pawn: return 3;
                case PieceType.Knight:
                case PieceType.Bishop: return 1;
                default: return 0;
            }
        }

        public static T Pick<T>(IList<T> items, Func<T, int> weight, Random rng) where T : class
        {
            int total = 0;
            foreach (var item in items) total += Math.Max(0, weight(item));
            if (total <= 0) return null;
            int roll = rng.Next(total);
            foreach (var item in items)
            {
                roll -= Math.Max(0, weight(item));
                if (roll < 0) return item;
            }
            return null;
        }

        /// <summary>不放回地抽取最多 count 个。</summary>
        public static List<T> PickMany<T>(IList<T> items, Func<T, int> weight, int count, Random rng) where T : class
        {
            var pool = new List<T>(items);
            var picked = new List<T>();
            while (picked.Count < count)
            {
                var item = Pick(pool, weight, rng);
                if (item == null) break;
                picked.Add(item);
                pool.Remove(item);
            }
            return picked;
        }

        public static bool AnyWeighted<T>(IEnumerable<T> items, Func<T, int> weight)
        {
            foreach (var item in items)
                if (weight(item) > 0) return true;
            return false;
        }
    }
}
