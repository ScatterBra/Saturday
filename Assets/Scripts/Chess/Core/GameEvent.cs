using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    public enum GameEventType
    {
        /// <summary>棋子移动（包括从城堡里走出来）。</summary>
        Moved,
        /// <summary>棋子被吃或被规则消灭。Text 是原因（captured / burned）。</summary>
        Captured,
        Promoted,
        /// <summary>被吃的棋子回到棋盘。</summary>
        Revived,
        /// <summary>棋子藏进城堡。</summary>
        EnteredCastle,
        /// <summary>外观、冻结、城堡等显示状态变了，表现层刷新这枚棋子即可。</summary>
        AppearanceChanged,
        LavaAdded,
        TurnPassed,
        StageStarted,
        RulesOffered,
        RuleActivated,
        TargetRequested,
        Dialogue,
        GameOver,
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

        public static GameEvent EnteredCastle(Piece guest, Coord from, Piece castle) =>
            new GameEvent
            {
                Type = GameEventType.EnteredCastle, PieceId = guest.Id, SourcePieceId = castle.Id,
                From = from, To = castle.Pos, Side = guest.Side,
            };

        public static GameEvent AppearanceChanged(Piece piece) =>
            new GameEvent { Type = GameEventType.AppearanceChanged, PieceId = piece.Id, To = piece.Pos, Side = piece.Side };

        public static GameEvent LavaAdded(IReadOnlyList<Coord> squares) =>
            new GameEvent { Type = GameEventType.LavaAdded, Squares = squares };

        public static GameEvent TurnPassed(Side side) =>
            new GameEvent { Type = GameEventType.TurnPassed, Side = side };

        public static GameEvent StageStarted(int stage) =>
            new GameEvent { Type = GameEventType.StageStarted, Stage = stage };

        public static GameEvent RulesOffered(int stage, IReadOnlyList<RuleBase> rules) =>
            new GameEvent { Type = GameEventType.RulesOffered, Stage = stage, Rules = rules };

        public static GameEvent RuleActivated(RuleBase rule) =>
            new GameEvent { Type = GameEventType.RuleActivated, Rule = rule, Stage = rule.Stage };

        public static GameEvent TargetRequested(TargetRequest request) =>
            new GameEvent { Type = GameEventType.TargetRequested, Request = request, Squares = request.Options, Text = request.Prompt };

        public static GameEvent Dialogue(string speaker, string text) =>
            new GameEvent { Type = GameEventType.Dialogue, Speaker = speaker, Text = text };

        public static GameEvent GameOver(Side? winner, string text) =>
            new GameEvent { Type = GameEventType.GameOver, Winner = winner, Text = text };

        public override string ToString() => $"{Type} piece={PieceId} {From}->{To} {Text}";
    }
}
