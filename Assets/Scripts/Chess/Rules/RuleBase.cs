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
    /// 一条家规。规则只在生效时修改棋盘状态（冻结、外观、城堡、岩浆……），不改写走法生成，
    /// 所以 AI 不需要知道有哪些规则。
    /// </summary>
    public abstract class RuleBase
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract RuleKind Kind { get; }
        /// <summary>0 = 开局，1 = 中局，2 = 残局。</summary>
        public abstract int Stage { get; }

        /// <summary>当前局面下能不能出现在抽卡里（例如马都被吃光了，龙就不出现）。</summary>
        public virtual bool IsAvailable(RuleContext ctx) => true;

        /// <summary>
        /// 规则生效。需要玩家点选目标时 yield 一个 TargetRequest，等玩家选完（Result 被填好）再继续。
        /// </summary>
        public abstract IEnumerable<TargetRequest> Activate(RuleContext ctx);

        /// <summary>每走完一步（或跳过一回合）调用一次。</summary>
        public virtual void OnTurnEnded(RuleContext ctx, Side sideThatMoved) { }

        public override string ToString() => Id;
    }

    /// <summary>规则请求玩家在棋盘上点选一个格子。</summary>
    public sealed class TargetRequest
    {
        public string Prompt;
        public IReadOnlyList<Coord> Options;
        /// <summary>可以跳过（不选）。</summary>
        public bool AllowSkip;
        /// <summary>玩家的选择；跳过时为 null。</summary>
        public Coord? Result;
    }

    /// <summary>
    /// 规则操作棋盘的入口。所有改动都通过这里，保证每个改动都会发出对应的事件给表现层。
    /// </summary>
    public sealed class RuleContext
    {
        readonly Action<GameEvent> emit;

        public Board Board { get; }
        public Random Rng { get; }
        public Side Player { get; }
        public Side Opponent => Player.Opponent();

        public RuleContext(Board board, Random rng, Side player, Action<GameEvent> emit)
        {
            Board = board;
            Rng = rng;
            Player = player;
            this.emit = emit;
        }

        public void Emit(GameEvent e) => emit(e);

        public void Say(string speaker, string text) => emit(GameEvent.Dialogue(speaker, text));

        /// <summary>这一方在棋盘上看得见的、指定类型的棋子。</summary>
        public List<Piece> Visible(Side side, params PieceType[] types)
        {
            var list = new List<Piece>();
            foreach (var p in Board.PiecesOf(side))
            {
                if (!p.IsVisible) continue;
                if (types.Length == 0 || Array.IndexOf(types, p.Type) >= 0) list.Add(p);
            }
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
            if (piece.Type == PieceType.King || !piece.IsVisible) return;
            piece.FrozenTurns = Math.Max(piece.FrozenTurns, turns);
            piece.FrozenSkin = frozenSkin;
            emit(GameEvent.AppearanceChanged(piece));
        }

        /// <summary>用规则效果直接消灭一枚棋子（不能是王，也不能是城堡）。</summary>
        public void Destroy(Piece piece, string cause, Piece source = null)
        {
            if (!piece.IsTargetable) throw new InvalidOperationException($"{piece} cannot be destroyed by a rule");
            var at = piece.Pos;
            Board.RemovePiece(piece);
            emit(GameEvent.Captured(piece, at, cause, source));
        }

        public void Revive(Piece piece, Coord at, string skin)
        {
            Board.RestorePiece(piece, at);
            piece.Skin = skin;
            emit(GameEvent.Revived(piece));
        }

        public void AddLava(IReadOnlyList<Coord> squares)
        {
            foreach (var c in squares) Board.SetLava(c);
            emit(GameEvent.LavaAdded(squares));
        }

        public void MakeCastle(Piece rook)
        {
            rook.IsCastle = true;
            rook.Skin = "castle";
            emit(GameEvent.AppearanceChanged(rook));
        }

        public void HideInCastle(Piece guest, Piece castle)
        {
            var from = guest.Pos;
            Board.HideInCastle(guest, castle);
            emit(GameEvent.EnteredCastle(guest, from, castle));
        }
    }
}
