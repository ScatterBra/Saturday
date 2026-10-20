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
        /// <summary>被吃掉的棋子（第三阶段对手打我方时不吃，而是击飞，见 KnockedBack）。</summary>
        public Piece Captured;
        public bool Promoted;

        /// <summary>第三阶段被击飞的我方棋子，以及它的起点和落点。</summary>
        public Piece KnockedBack;
        public Coord KnockFrom;
        public Coord KnockTo;
        /// <summary>被击飞到岩浆里烧掉了（可以复活）。</summary>
        public bool KnockedIntoLava;
        /// <summary>不死的棋子被击飞到岩浆里：没死，禁止行动一回合。</summary>
        public bool Scorched;

        /// <summary>走进岩浆献祭了。</summary>
        public bool Sacrificed;
        /// <summary>这一步献祭凑够了数量：岩浆从此可以站、可以穿过。</summary>
        public bool TaskCompleted;
        /// <summary>马走进了城堡：救出公主。</summary>
        public bool Rescued;

        public bool KingCaptured => Captured != null && Captured.Type == PieceType.King;
    }
}
