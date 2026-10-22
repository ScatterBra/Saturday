namespace SaturdayGame.Chess
{
    /// <summary>
    /// 一枚棋子。规则不会改写走法，只会修改这里的状态（冻结、不死、外观），
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
        /// <summary>被吃掉（或被献祭、被风暴卷走）后为 false。</summary>
        public bool OnBoard = true;
        /// <summary>被献祭进岩浆：永远不会回来，也不进被吃区。</summary>
        public bool Sacrificed;
        /// <summary>第三阶段风暴来临时被卷走的旧棋子（对手原来的棋子）：不进被吃区。</summary>
        public bool Retired;
        /// <summary>
        /// 被吃过（包括被规则烧掉、被弹进岩浆）。复活后也不清除，表现层据此一直用纸片版的样子。
        /// </summary>
        public bool WasLost;

        /// <summary>外观 ID，null 表示默认外观。只给表现层用，不影响逻辑。后设置的外观覆盖之前的。</summary>
        public string Skin;

        /// <summary>还要冻结几个（自己一方的）回合。</summary>
        public int FrozenTurns;
        /// <summary>冻结时叠加的外观，例如 tree、poop。解冻后清空。</summary>
        public string FrozenSkin;

        /// <summary>
        /// 第三阶段每献祭两个棋子，玩家选一枚棋子变成不死：被弹进岩浆不会死，只是禁止行动一回合。
        /// （第三阶段朋友本来就不吃子，只击飞。）
        /// </summary>
        public bool Undying;

        public bool IsFrozen => FrozenTurns > 0;

        /// <summary>在棋盘上。</summary>
        public bool IsVisible => OnBoard;

        /// <summary>规则的随机效果（龙火、鸟群等）能不能选中它。</summary>
        public bool IsTargetable => OnBoard && Type != PieceType.King;

        /// <summary>被吃掉、可以复活的棋子（献祭的和被风暴卷走的不算）。</summary>
        public bool IsRevivable => !OnBoard && !Sacrificed && !Retired;

        public Piece Clone() => (Piece)MemberwiseClone();

        public override string ToString() => $"{Side} {Type} @{Pos}";
    }
}
