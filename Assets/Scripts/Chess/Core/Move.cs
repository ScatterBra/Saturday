using System;

namespace SaturdayGame.Chess
{
    public readonly struct Move : IEquatable<Move>
    {
        public readonly int PieceId;
        public readonly Coord From;
        public readonly Coord To;

        public Move(int pieceId, Coord from, Coord to)
        {
            PieceId = pieceId;
            From = from;
            To = to;
        }

        public bool Equals(Move other) => PieceId == other.PieceId && From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => (PieceId * 397) ^ (From.Index * 64 + To.Index);
        public override string ToString() => $"#{PieceId} {From}->{To}";
    }

    public sealed class MoveResult
    {
        public Piece Mover;
        public Coord From;
        public Coord To;
        /// <summary>被吃掉的棋子。</summary>
        public Piece Captured;
        public bool Promoted;

        /// <summary>第三阶段：城门打开后玩家的棋子走进了城堡，夺权成功。</summary>
        public bool CastleTaken;

        public bool KingCaptured => Captured != null && Captured.Type == PieceType.King;
    }
}
