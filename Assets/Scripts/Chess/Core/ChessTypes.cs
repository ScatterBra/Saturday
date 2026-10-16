using System;

namespace SaturdayGame.Chess
{
    public enum Side { White, Black }

    public enum PieceType { Pawn, Knight, Bishop, Rook, Queen, King }

    public static class SideExtensions
    {
        public static Side Opponent(this Side side) => side == Side.White ? Side.Black : Side.White;

        /// <summary>兵前进的方向（白方向 y 增大的方向走）。</summary>
        public static int Forward(this Side side) => side == Side.White ? 1 : -1;

        public static int HomeRank(this Side side) => side == Side.White ? 0 : 7;

        /// <summary>兵的起始行，兵在这一行可以走两格。</summary>
        public static int PawnRank(this Side side) => side == Side.White ? 1 : 6;

        public static int PromotionRank(this Side side) => side == Side.White ? 7 : 0;
    }

    /// <summary>
    /// 棋盘坐标。X 是列（0..7 对应 a..h），Y 是行（0..7 对应 1..8），白方在 Y = 0 一侧。
    /// </summary>
    public readonly struct Coord : IEquatable<Coord>
    {
        public readonly int X;
        public readonly int Y;

        public Coord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool IsValid => X >= 0 && X < 8 && Y >= 0 && Y < 8;
        public int Index => Y * 8 + X;

        public static Coord FromIndex(int index) => new Coord(index % 8, index / 8);

        /// <summary>切比雪夫距离：王走几步能到。</summary>
        public int DistanceTo(Coord other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        public static Coord operator +(Coord a, Coord b) => new Coord(a.X + b.X, a.Y + b.Y);
        public static bool operator ==(Coord a, Coord b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Coord a, Coord b) => !(a == b);

        public bool Equals(Coord other) => this == other;
        public override bool Equals(object obj) => obj is Coord other && this == other;
        public override int GetHashCode() => Index;
        public override string ToString() => IsValid ? $"{(char)('a' + X)}{Y + 1}" : $"({X},{Y})";
    }

    /// <summary>
    /// 棋子价值（国际象棋常用估值），AI 评估和规则的随机加权都用它。
    /// </summary>
    public static class PieceValues
    {
        public static int Of(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return 1;
                case PieceType.Knight: return 3;
                case PieceType.Bishop: return 3;
                case PieceType.Rook: return 5;
                case PieceType.Queen: return 9;
                default: return 0; // 王无价，单独处理
            }
        }

        public static string Letter(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return "P";
                case PieceType.Knight: return "N";
                case PieceType.Bishop: return "B";
                case PieceType.Rook: return "R";
                case PieceType.Queen: return "Q";
                default: return "K";
            }
        }
    }
}
