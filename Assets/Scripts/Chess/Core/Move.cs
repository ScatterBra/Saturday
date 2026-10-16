using System;

namespace SaturdayGame.Chess
{
    public readonly struct Move : IEquatable<Move>
    {
        public readonly int PieceId;
        public readonly Coord From;
        public readonly Coord To;
        /// <summary>藏在城堡里的棋子从城堡里走出来。</summary>
        public readonly bool GuestExit;

        public Move(int pieceId, Coord from, Coord to, bool guestExit = false)
        {
            PieceId = pieceId;
            From = from;
            To = to;
            GuestExit = guestExit;
        }

        public bool Equals(Move other) =>
            PieceId == other.PieceId && From == other.From && To == other.To && GuestExit == other.GuestExit;

        public override bool Equals(object obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => (PieceId * 397) ^ (From.Index * 64 + To.Index) ^ (GuestExit ? 1 << 20 : 0);
        public override string ToString() => $"#{PieceId} {From}->{To}{(GuestExit ? " (exit castle)" : "")}";
    }

    public sealed class MoveResult
    {
        public Piece Mover;
        public Coord From;
        public Coord To;
        public Piece Captured;
        public bool Promoted;
        public bool GuestExit;

        public bool KingCaptured => Captured != null && Captured.Type == PieceType.King;
    }
}
