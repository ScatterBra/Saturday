using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    public enum GamePhase
    {
        PlayerTurn,
        OpponentTurn,
        /// <summary>等玩家从三张规则卡里选一张。</summary>
        ChoosingRule,
        /// <summary>规则生效到一半，等玩家在棋盘上点选目标。</summary>
        ChoosingTarget,
        GameOver,
    }

    public sealed class GameSettings
    {
        public int Seed = Environment.TickCount;
        /// <summary>在这些回合开始时（轮到玩家走之前）抽规则卡，一个阶段一次。</summary>
        public int[] StageTurns = { 4, 10, 16 };
        /// <summary>超过这个回合数判平局，0 表示不限。目前只给测试用。</summary>
        public int MaxFullMoves;
    }

    /// <summary>
    /// 一整局棋：棋盘 + 阶段 + 规则 + 胜负。玩家固定执白（下方），对手是朋友（AI，执黑）。
    /// 对手不使用规则，只遵守规则。
    /// 所有操作都会往事件队列里加事件，表现层用 DrainEvents() 取出来按顺序演出。
    /// </summary>
    public sealed class ChessGame
    {
        readonly GameSettings settings;
        readonly List<GameEvent> events = new List<GameEvent>();
        readonly List<RuleBase> activeRules = new List<RuleBase>();
        readonly RuleContext ctx;
        List<RuleBase> offers;
        IEnumerator<TargetRequest> activation;
        int nextStage;
        int passesInARow;

        public Board Board { get; }
        public Random Rng { get; }
        public Side PlayerSide => Side.White;
        public Side OpponentSide => Side.Black;
        public GamePhase Phase { get; private set; }
        public Side? Winner { get; private set; }

        /// <summary>已经生效的规则，按生效顺序排列（规则书）。</summary>
        public IReadOnlyList<RuleBase> ActiveRules => activeRules;
        /// <summary>ChoosingRule 阶段可选的规则卡。</summary>
        public IReadOnlyList<RuleBase> Offers => offers;
        /// <summary>ChoosingTarget 阶段等待的点选请求。</summary>
        public TargetRequest PendingTarget { get; private set; }
        /// <summary>下一个要触发的阶段（0..3，3 表示都触发过了）。</summary>
        public int NextStage => nextStage;
        public int NextStageTurn => nextStage < settings.StageTurns.Length ? settings.StageTurns[nextStage] : -1;

        public ChessGame(GameSettings settings = null)
        {
            this.settings = settings ?? new GameSettings();
            Rng = new Random(this.settings.Seed);
            Board = Board.CreateStandard();
            ctx = new RuleContext(Board, Rng, PlayerSide, events.Add);
            BeginTurn();
        }

        /// <summary>取出并清空目前积累的事件。</summary>
        public List<GameEvent> DrainEvents()
        {
            var list = new List<GameEvent>(events);
            events.Clear();
            return list;
        }

        /// <summary>当前走子方的所有合法走法。</summary>
        public List<Move> LegalMoves() =>
            IsMovePhase ? MoveGenerator.Generate(Board, Board.SideToMove) : new List<Move>();

        /// <summary>某枚棋子当前的合法走法（不是它走的时候返回空）。</summary>
        public List<Move> MovesFor(Piece piece)
        {
            if (!IsMovePhase || piece.Side != Board.SideToMove) return new List<Move>();
            return MoveGenerator.MovesFor(Board, piece);
        }

        bool IsMovePhase => Phase == GamePhase.PlayerTurn || Phase == GamePhase.OpponentTurn;

        public MoveResult PlayMove(Move move)
        {
            if (!IsMovePhase) throw new InvalidOperationException($"Cannot move during {Phase}");
            if (!MoveGenerator.Generate(Board, Board.SideToMove).Contains(move))
                throw new InvalidOperationException($"Illegal move {move}");

            var side = Board.SideToMove;
            var mover = Board.GetPiece(move.PieceId);
            var result = Board.Apply(move);
            passesInARow = 0;

            events.Add(GameEvent.Moved(mover, move.From, move.To));
            if (result.Captured != null) events.Add(GameEvent.Captured(result.Captured, move.To, "captured", mover));
            if (result.Promoted) events.Add(GameEvent.Promoted(mover));

            if (result.KingCaptured)
            {
                EndGame(side, side == PlayerSide ? "You captured the king!" : "Your friend captured your king.");
                return result;
            }

            foreach (var rule in activeRules) rule.OnTurnEnded(ctx, side);
            BeginTurn();
            return result;
        }

        public void ChooseRule(int index)
        {
            if (Phase != GamePhase.ChoosingRule) throw new InvalidOperationException($"Cannot choose a rule during {Phase}");
            if (index < 0 || index >= offers.Count) throw new ArgumentOutOfRangeException(nameof(index));

            var rule = offers[index];
            offers = null;
            activeRules.Add(rule);
            events.Add(GameEvent.RuleActivated(rule));
            activation = rule.Activate(ctx).GetEnumerator();
            ContinueActivation();
        }

        /// <summary>回应规则的点选请求。传 null 表示跳过（只有允许跳过时才行）。</summary>
        public void ResolveTarget(Coord? choice)
        {
            if (Phase != GamePhase.ChoosingTarget) throw new InvalidOperationException($"No target requested during {Phase}");
            if (choice.HasValue ? !Contains(PendingTarget.Options, choice.Value) : !PendingTarget.AllowSkip)
                throw new ArgumentException($"Invalid target {choice}");

            PendingTarget.Result = choice;
            ContinueActivation();
        }

        void ContinueActivation()
        {
            if (activation.MoveNext())
            {
                PendingTarget = activation.Current;
                Phase = GamePhase.ChoosingTarget;
                events.Add(GameEvent.TargetRequested(PendingTarget));
                return;
            }
            activation.Dispose();
            activation = null;
            PendingTarget = null;
            BeginTurn();
        }

        /// <summary>
        /// 进入下一个可以行动的状态：该抽规则卡就抽卡；走子方没有合法走法就跳过；双方都动不了判平局。
        /// </summary>
        void BeginTurn()
        {
            while (true)
            {
                if (settings.MaxFullMoves > 0 && Board.FullMove > settings.MaxFullMoves)
                {
                    EndGame(null, "Turn limit reached.");
                    return;
                }

                if (Board.SideToMove == PlayerSide && nextStage < settings.StageTurns.Length &&
                    nextStage < RuleCatalog.StageCount && Board.FullMove >= settings.StageTurns[nextStage])
                {
                    int stage = nextStage++;
                    events.Add(GameEvent.StageStarted(stage));
                    offers = RuleCatalog.CreatePool(stage);
                    offers.RemoveAll(r => !r.IsAvailable(ctx));
                    if (offers.Count > 0)
                    {
                        events.Add(GameEvent.Dialogue(Speakers.Friend, StageLine(stage)));
                        events.Add(GameEvent.RulesOffered(stage, offers));
                        Phase = GamePhase.ChoosingRule;
                        return;
                    }
                    offers = null;
                    continue;
                }

                if (MoveGenerator.HasAnyMove(Board, Board.SideToMove))
                {
                    Phase = Board.SideToMove == PlayerSide ? GamePhase.PlayerTurn : GamePhase.OpponentTurn;
                    return;
                }

                // 没有合法走法：跳过这一回合
                var stuck = Board.SideToMove;
                events.Add(GameEvent.TurnPassed(stuck));
                if (++passesInARow >= 2)
                {
                    EndGame(null, "Nobody can move. I think we broke it.");
                    return;
                }
                Board.Pass();
                foreach (var rule in activeRules) rule.OnTurnEnded(ctx, stuck);
            }
        }

        void EndGame(Side? winner, string text)
        {
            Winner = winner;
            Phase = GamePhase.GameOver;
            events.Add(GameEvent.GameOver(winner, text));
        }

        static string StageLine(int stage)
        {
            switch (stage)
            {
                case 0: return "Wait. We don't have to play like that. Pick a new rule.";
                case 1: return "Your turn to change something.";
                default: return "Let's make it weirder.";
            }
        }

        static bool Contains(IReadOnlyList<Coord> list, Coord c)
        {
            foreach (var item in list)
                if (item == c) return true;
            return false;
        }
    }
}
