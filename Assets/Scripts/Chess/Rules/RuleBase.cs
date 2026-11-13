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
        /// <summary>规则卡上的完整说明。</summary>
        public abstract string Description { get; }
        /// <summary>
        /// 一直生效的规则写进右边规则书的一两句话；null = 一次性的技能，用完就不进规则书（说明只在规则卡上）。
        /// 细节交给棋子的悬浮窗（PieceTooltip）。
        /// </summary>
        public virtual string RuleBookText => null;
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

        /// <summary>点选格子时，在鼠标停的格子上预览这枚棋子（例如复活前看看它会出现在哪），以及它到时候的外观。</summary>
        public int? PreviewPieceId;
        public string PreviewSkin;

        /// <summary>
        /// 点选格子时，鼠标停在某个可选格子上会打到哪些格子（例如光束经过的斜线、雷暴的 4 × 4 范围），表现层用来预览；
        /// 为 null 时不预览。
        /// </summary>
        public Func<Coord, IReadOnlyList<Coord>> Affected;

        /// <summary>
        /// 点选格子时像下棋一样显示：Origin 上的棋子标成选中，可选格子按走法上色（空格绿、敌人红、自己人橙），
        /// 点可选格子以外的地方就取消（像下棋时点空白处取消选中）。为 null 时照普通点选显示。
        /// </summary>
        public Coord? Origin;

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

        /// <summary>
        /// 这一步走完后，消防栓、青蛙优先喷或拉这枚棋子（玩家在预览里看到的目标，见 ChessGame.PlayMove）。
        /// 只在 OnPieceMoved 期间有值。
        /// </summary>
        public int? PreferredTargetId;

        /// <summary>
        /// 技能真的放出去了（没有在点选时跳过）。第三阶段的常驻技能放出去就结束这一回合、开始冷却；
        /// 手里的卡放出去才算用掉。由 ChessGame 在每次放技能前清零。
        /// </summary>
        public bool Acted;

        /// <summary>
        /// 放技能的棋子 Id（第三阶段点了象、马头顶的技能图标）：技能只用这枚棋子，跳过“选哪个象 / 马”那一步。
        /// 为 null 时照常让玩家挑。
        /// </summary>
        public int? Caster;

        /// <summary>技能额外给雷暴积累条加的格数（例如自爆），ChessGame 用 TakeThunderCharge 取走。</summary>
        int thunderCharge;

        public void AddThunderCharge(int amount) => thunderCharge += amount;

        public int TakeThunderCharge()
        {
            int amount = thunderCharge;
            thunderCharge = 0;
            return amount;
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
            emit(GameEvent.AppearanceChanged(piece, skin));
        }

        /// <summary>临时变身结束，变回原来的外观 skin（表现层播变回去的特效和音效）。</summary>
        public void RevertSkin(Piece piece, string skin)
        {
            piece.Skin = skin;
            emit(GameEvent.AppearanceChanged(piece, skin, reverted: true));
        }

        /// <summary>冻结棋子若干个（它自己一方的）回合。王永远不会被冻结。</summary>
        public void Freeze(Piece piece, int turns, string frozenSkin)
        {
            if (piece.Type == PieceType.King || !piece.OnBoard) return;
            piece.FrozenTurns = Math.Max(piece.FrozenTurns, turns);
            piece.FrozenSkin = frozenSkin;
            emit(GameEvent.AppearanceChanged(piece, frozenSkin, frozen: true));
        }

        /// <summary>用规则效果直接消灭一枚棋子（不能是王）。</summary>
        public void Destroy(Piece piece, string cause, Piece source = null)
        {
            if (!piece.IsTargetable) throw new InvalidOperationException($"{piece} cannot be destroyed by a rule");
            var at = piece.Pos;
            Board.RemovePiece(piece);
            emit(GameEvent.Captured(piece, at, cause, source));
        }

        /// <summary>第三阶段的技能直接消灭一枚棋子（王也行，王在第三阶段是普通棋子）。cause 见 GameEvent.Captured。</summary>
        public void Kill(Piece piece, string cause, Piece source = null)
        {
            if (!piece.OnBoard) return;
            var at = piece.Pos;
            Board.RemovePiece(piece);
            emit(GameEvent.Captured(piece, at, cause, source));
        }

        /// <summary>火攻（龙火）：烧掉这枚棋子；它是潮湿的就在原地爆炸。</summary>
        public void Burn(Piece victim, Piece source)
        {
            bool wet = victim.IsWet;
            var at = victim.Pos;
            Kill(victim, "burned", source);
            if (wet) Explode(at);
        }

        /// <summary>
        /// 爆炸（潮湿遇火、自爆）：身边一圈的棋子各往远离中心的方向推 1 格，敌我都推，不炸死。
        /// 落点有棋子、出界或是城堡就留在原地；落点是岩浆就烧掉（不连锁：潮湿的也不会再炸）。城堡里的棋子不受影响。
        /// 每个棋子都往外走，落点不会互相重叠，推的顺序不影响结果。最后中心留下熔岩坑（城堡周围一圈除外）。
        /// </summary>
        public void Explode(Coord center)
        {
            emit(GameEvent.Exploded(center));
            for (int dy = 1; dy >= -1; dy--)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var dir = new Coord(dx, dy);
                var from = center + dir;
                if (!from.IsValid) continue;
                var piece = Board.PieceAt(from);
                if (piece == null || Board.InCastle(piece)) continue;
                var to = from + dir;
                if (!to.IsValid || Board.IsCastle(to) || !Board.IsEmpty(to))
                {
                    emit(GameEvent.KnockedBack(piece, from, from, null));
                    continue;
                }
                if (Board.IsLava(to))
                {
                    Board.BurnInLava(piece, to);
                    emit(GameEvent.KnockedBack(piece, from, to, null));
                    emit(GameEvent.Captured(piece, to, "lava"));
                    continue;
                }
                Board.Relocate(piece, to);
                emit(GameEvent.KnockedBack(piece, from, to, null));
                PromoteIfNeeded(piece);
            }
            if (Board.CanBecomeLavaPit(center)) AddLava(new[] { center });
        }

        /// <summary>让这些棋子潮湿 turns 个回合（城堡里的不算）。</summary>
        public void Soak(IEnumerable<Piece> pieces, int turns)
        {
            var squares = new List<Coord>();
            foreach (var p in pieces)
            {
                if (!p.OnBoard || Board.InCastle(p)) continue;
                p.WetTurns = Math.Max(p.WetTurns, turns);
                squares.Add(p.Pos);
            }
            emit(GameEvent.Soaked(squares));
        }

        /// <summary>复活棋子。skin 为 null 时保留原来的外观。</summary>
        public void Revive(Piece piece, Coord at, string skin)
        {
            Board.RestorePiece(piece, at);
            if (skin != null) piece.Skin = skin;
            emit(GameEvent.Revived(piece));
        }

        /// <summary>
        /// 把棋子推到另一个空格（消防栓、青蛙、山崩）；推到岩浆格就烧掉，潮湿的还会在岩浆上爆炸。
        /// 兵被推到对方底线时照常升变成后。
        /// </summary>
        public void Push(Piece piece, Coord to, Piece source)
        {
            var from = piece.Pos;
            if (Board.IsLava(to))
            {
                bool wet = piece.IsWet;
                Board.BurnInLava(piece, to);
                emit(GameEvent.Pushed(piece, from, to, source));
                emit(GameEvent.Captured(piece, to, "lava", source));
                if (wet) Explode(to);
                return;
            }
            Board.Relocate(piece, to);
            emit(GameEvent.Pushed(piece, from, to, source));
            PromoteIfNeeded(piece);
        }

        void PromoteIfNeeded(Piece piece)
        {
            if (piece.Type != PieceType.Pawn || piece.Pos.Y != piece.Side.PromotionRank()) return;
            piece.Type = PieceType.Queen;
            emit(GameEvent.Promoted(piece));
        }

        public void AddLava(IReadOnlyList<Coord> squares)
        {
            foreach (var c in squares) Board.SetLava(c);
            emit(GameEvent.LavaAdded(squares));
        }
    }
}
