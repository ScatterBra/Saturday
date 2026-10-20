using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    public enum RuleKind
    {
        /// <summary>只改外观。</summary>
        Cosmetic,
        /// <summary>强化自己。</summary>
        Buff,
        /// <summary>弱化对手。</summary>
        Debuff,
        /// <summary>双方都受影响（岩浆）。</summary>
        Neutral,
    }

    public static class Speakers
    {
        public const string Friend = "Friend";
        public const string You = "You";
    }

    /// <summary>
    /// 一条家规。规则只在生效时修改棋盘状态（冻结、外观、复活……），不改写走法生成，
    /// 所以 AI 不需要知道有哪些规则。
    /// </summary>
    public abstract class RuleBase
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract RuleKind Kind { get; }
        /// <summary>0 = 开局，1 = 中局，2 = 第三阶段（城堡营救）。</summary>
        public abstract int Stage { get; }

        /// <summary>当前局面下能不能出现在抽卡里（例如马都被吃光了，龙就不出现）。</summary>
        public virtual bool IsAvailable(RuleContext ctx) => true;

        /// <summary>
        /// 规则生效。需要玩家点选目标时 yield 一个 TargetRequest，等玩家选完（Result 被填好）再继续。
        /// </summary>
        public abstract IEnumerable<TargetRequest> Activate(RuleContext ctx);

        /// <summary>
        /// 任何一方的棋子走完一步、落在新格子上之后调用（献祭进岩浆的不算）。
        /// 例如消防栓落地后喷开旁边的棋子。
        /// </summary>
        public virtual void OnPieceMoved(RuleContext ctx, Piece mover, Coord from) { }

        /// <summary>每走完一步（或跳过一回合）调用一次。</summary>
        public virtual void OnTurnEnded(RuleContext ctx, Side sideThatMoved) { }

        public override string ToString() => Id;
    }

    /// <summary>
    /// 规则请求玩家点选：棋盘上的一个格子（Options），或者被吃区里的一枚棋子（PieceOptions）。
    /// </summary>
    public sealed class TargetRequest
    {
        public string Prompt;
        /// <summary>可选的棋盘格子；点选被吃棋子时为 null。</summary>
        public IReadOnlyList<Coord> Options;
        /// <summary>可选的被吃棋子（棋子 Id）；点选格子时为 null。</summary>
        public IReadOnlyList<int> PieceOptions;
        /// <summary>可以跳过（不选）。</summary>
        public bool AllowSkip;
        /// <summary>玩家选的格子；跳过时为 null。</summary>
        public Coord? Result;
        /// <summary>玩家选的棋子 Id；跳过时为 null。</summary>
        public int? PieceResult;

        public bool IsPieceRequest => PieceOptions != null;
    }

    /// <summary>
    /// 规则操作棋盘的入口。所有改动都通过这里，保证每个改动都会发出对应的事件给表现层。
    /// Player 是放这个技能的一方：玩家选的规则 Player = 白方；朋友放的技能 Player = 黑方。
    /// </summary>
    public sealed class RuleContext
    {
        readonly Action<GameEvent> emit;

        public Board Board { get; }
        public Random Rng { get; }
        public Side Player { get; }
        public Side Opponent => Player.Opponent();
        /// <summary>这个技能是朋友（AI）放的。</summary>
        public bool CastByFriend => Player == Side.Black;

        public RuleContext(Board board, Random rng, Side player, Action<GameEvent> emit)
        {
            Board = board;
            Rng = rng;
            Player = player;
            this.emit = emit;
        }

        public void Emit(GameEvent e) => emit(e);

        public void Say(string speaker, string text) => emit(GameEvent.Dialogue(speaker, text));

        /// <summary>这一方在棋盘上的、指定类型的棋子。</summary>
        public List<Piece> Visible(Side side, params PieceType[] types)
        {
            var list = new List<Piece>();
            foreach (var p in Board.PiecesOf(side))
                if (types.Length == 0 || Array.IndexOf(types, p.Type) >= 0) list.Add(p);
            return list;
        }

        public void SetSkin(Piece piece, string skin)
        {
            piece.Skin = skin;
            emit(GameEvent.AppearanceChanged(piece));
        }

        /// <summary>冻结棋子若干个（它自己一方的）回合。王永远不会被冻结。</summary>
        public void Freeze(Piece piece, int turns, string frozenSkin)
        {
            if (piece.Type == PieceType.King || !piece.OnBoard) return;
            piece.FrozenTurns = Math.Max(piece.FrozenTurns, turns);
            piece.FrozenSkin = frozenSkin;
            emit(GameEvent.AppearanceChanged(piece));
        }

        /// <summary>用规则效果直接消灭一枚棋子（不能是王）。</summary>
        public void Destroy(Piece piece, string cause, Piece source = null)
        {
            if (!piece.IsTargetable) throw new InvalidOperationException($"{piece} cannot be destroyed by a rule");
            var at = piece.Pos;
            Board.RemovePiece(piece);
            emit(GameEvent.Captured(piece, at, cause, source));
        }

        /// <summary>复活棋子。skin 为 null 时保留原来的外观。</summary>
        public void Revive(Piece piece, Coord at, string skin)
        {
            Board.RestorePiece(piece, at);
            if (skin != null) piece.Skin = skin;
            emit(GameEvent.Revived(piece));
        }

        /// <summary>把棋子推到另一个空格。兵被推到对方底线时照常升变成后。</summary>
        public void Push(Piece piece, Coord to, Piece source)
        {
            var from = piece.Pos;
            Board.Relocate(piece, to);
            emit(GameEvent.Pushed(piece, from, to, source));
            if (piece.Type == PieceType.Pawn && to.Y == piece.Side.PromotionRank())
            {
                piece.Type = PieceType.Queen;
                emit(GameEvent.Promoted(piece));
            }
        }

        public void AddLava(IReadOnlyList<Coord> squares)
        {
            foreach (var c in squares) Board.SetLava(c);
            emit(GameEvent.LavaAdded(squares));
        }
    }
}
