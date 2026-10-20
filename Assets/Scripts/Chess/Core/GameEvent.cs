using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    public enum GameEventType
    {
        /// <summary>棋子移动。</summary>
        Moved,
        /// <summary>棋子被吃、被规则消灭，或被击飞进岩浆。Text 是原因（captured / burned / lava）。</summary>
        Captured,
        Promoted,
        /// <summary>被吃的棋子回到棋盘。</summary>
        Revived,
        /// <summary>外观、冻结等显示状态变了，表现层刷新这枚棋子即可。</summary>
        AppearanceChanged,
        LavaAdded,
        TurnPassed,
        StageStarted,
        RulesOffered,
        RuleActivated,
        TargetRequested,
        Dialogue,
        GameOver,
        /// <summary>某一方被将军（王正被攻击）。</summary>
        Check,
        /// <summary>第三阶段：对手掀起风暴。</summary>
        Storm,
        /// <summary>第三阶段：风暴过后棋盘换成新的地形和阵型，表现层整体重建。</summary>
        BoardRebuilt,
        /// <summary>第三阶段：棋子走进岩浆献祭。</summary>
        Sacrificed,
        /// <summary>第三阶段：我方棋子被击飞（From → To）。</summary>
        KnockedBack,
        /// <summary>对手放了一个技能（Rule）。</summary>
        OpponentSkill,
    }

    /// <summary>规则卡是在什么情况下弹出的。</summary>
    public enum OfferKind
    {
        /// <summary>第 4、10 回合的固定选卡（必须选）。</summary>
        StageCard,
        /// <summary>被将军时的救急技能（可以跳过）。</summary>
        Emergency,
        /// <summary>第三阶段的技能（可以跳过）。</summary>
        Stage3,
    }

    /// <summary>
    /// 逻辑层发给表现层的事件。逻辑瞬间算完，表现层按顺序把事件演出来。
    /// </summary>
    public sealed class GameEvent
    {
        public GameEventType Type;
        public int PieceId = -1;
        /// <summary>造成效果的棋子，例如喷火的龙。</summary>
        public int SourcePieceId = -1;
        public Coord From;
        public Coord To;
        public Side Side;
        /// <summary>GameOver 时的赢家，平局为 null。</summary>
        public Side? Winner;
        public int Stage = -1;
        public OfferKind OfferKind;
        public string Speaker;
        public string Text;
        public RuleBase Rule;
        public IReadOnlyList<RuleBase> Rules;
        public IReadOnlyList<Coord> Squares;
        public TargetRequest Request;

        public static GameEvent Moved(Piece piece, Coord from, Coord to) =>
            new GameEvent { Type = GameEventType.Moved, PieceId = piece.Id, From = from, To = to, Side = piece.Side };

        public static GameEvent Captured(Piece piece, Coord at, string cause, Piece source = null) =>
            new GameEvent
            {
                Type = GameEventType.Captured, PieceId = piece.Id, To = at, Side = piece.Side, Text = cause,
                SourcePieceId = source?.Id ?? -1,
            };

        public static GameEvent Promoted(Piece piece) =>
            new GameEvent { Type = GameEventType.Promoted, PieceId = piece.Id, To = piece.Pos, Side = piece.Side };

        public static GameEvent Revived(Piece piece) =>
            new GameEvent { Type = GameEventType.Revived, PieceId = piece.Id, To = piece.Pos, Side = piece.Side };

        public static GameEvent AppearanceChanged(Piece piece) =>
            new GameEvent { Type = GameEventType.AppearanceChanged, PieceId = piece.Id, To = piece.Pos, Side = piece.Side };

        public static GameEvent LavaAdded(IReadOnlyList<Coord> squares) =>
            new GameEvent { Type = GameEventType.LavaAdded, Squares = squares };

        public static GameEvent TurnPassed(Side side) =>
            new GameEvent { Type = GameEventType.TurnPassed, Side = side };

        public static GameEvent StageStarted(int stage) =>
            new GameEvent { Type = GameEventType.StageStarted, Stage = stage };

        public static GameEvent RulesOffered(int stage, OfferKind kind, IReadOnlyList<RuleBase> rules) =>
            new GameEvent { Type = GameEventType.RulesOffered, Stage = stage, OfferKind = kind, Rules = rules };

        public static GameEvent RuleActivated(RuleBase rule) =>
            new GameEvent { Type = GameEventType.RuleActivated, Rule = rule, Stage = rule.Stage };

        public static GameEvent TargetRequested(TargetRequest request) =>
            new GameEvent { Type = GameEventType.TargetRequested, Request = request, Squares = request.Options, Text = request.Prompt };

        public static GameEvent Dialogue(string speaker, string text) =>
            new GameEvent { Type = GameEventType.Dialogue, Speaker = speaker, Text = text };

        public static GameEvent GameOver(Side? winner, string text) =>
            new GameEvent { Type = GameEventType.GameOver, Winner = winner, Text = text };

        public static GameEvent Check(Side side) =>
            new GameEvent { Type = GameEventType.Check, Side = side };

        public static GameEvent Storm() => new GameEvent { Type = GameEventType.Storm };

        public static GameEvent BoardRebuilt() => new GameEvent { Type = GameEventType.BoardRebuilt };

        public static GameEvent Sacrificed(Piece piece, Coord from, Coord at) =>
            new GameEvent { Type = GameEventType.Sacrificed, PieceId = piece.Id, From = from, To = at, Side = piece.Side };

        public static GameEvent KnockedBack(Piece piece, Coord from, Coord to, Piece attacker) =>
            new GameEvent
            {
                Type = GameEventType.KnockedBack, PieceId = piece.Id, From = from, To = to, Side = piece.Side,
                SourcePieceId = attacker?.Id ?? -1,
            };

        public static GameEvent OpponentSkill(RuleBase rule) =>
            new GameEvent { Type = GameEventType.OpponentSkill, Rule = rule, Stage = rule.Stage };

        public override string ToString() => $"{Type} piece={PieceId} {From}->{To} {Text}";
    }
}
