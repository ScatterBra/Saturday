namespace SaturdayGame.Chess
{
    /// <summary>
    /// 一枚棋子。规则不会改写走法，只会修改这里的状态（冻结、城堡、藏身、外观），
    /// 走法生成器读取这些状态来决定能怎么走。
    /// </summary>
    public sealed class Piece
    {
        /// <summary>同时也是它在 Board.AllPieces 里的下标，整局不变。</summary>
        public int Id;
        public PieceType Type;
        public Side Side;
        public Coord Pos;
        /// <summary>开局时的位置，复活时尽量放回这里。</summary>
        public Coord StartPos;
        /// <summary>被吃掉后为 false。</summary>
        public bool OnBoard = true;

        /// <summary>外观 ID，null 表示默认外观。只给表现层用，不影响逻辑。后设置的外观覆盖之前的。</summary>
        public string Skin;

        /// <summary>还要冻结几个（自己一方的）回合。</summary>
        public int FrozenTurns;
        /// <summary>冻结时叠加的外观，例如 tree、poop。解冻后清空。</summary>
        public string FrozenSkin;

        /// <summary>变成城堡的车：不能被吃，可以藏一个友方棋子。</summary>
        public bool IsCastle;
        public int GuestId = -1;

        /// <summary>藏在城堡里：不占格子，不能被吃，Pos 跟随城堡。</summary>
        public bool IsHidden;
        public int HostId = -1;

        public bool IsFrozen => FrozenTurns > 0;
        public bool HasGuest => GuestId >= 0;

        /// <summary>在棋盘上看得见（没被吃，也没藏起来）。</summary>
        public bool IsVisible => OnBoard && !IsHidden;

        /// <summary>规则的随机效果（龙火、鸟群等）能不能选中它。</summary>
        public bool IsTargetable => IsVisible && !IsCastle && Type != PieceType.King;

        public Piece Clone() => (Piece)MemberwiseClone();

        public override string ToString() => $"{Side} {Type} @{Pos}";
    }
}
