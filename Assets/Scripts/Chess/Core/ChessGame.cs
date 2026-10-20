using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    public enum GamePhase
    {
        PlayerTurn,
        OpponentTurn,
        /// <summary>等玩家从弹出的规则卡（技能）里选一张。</summary>
        ChoosingRule,
        /// <summary>规则生效到一半，等玩家点选目标（棋盘格子或被吃的棋子）。</summary>
        ChoosingTarget,
        GameOver,
    }

    /// <summary>到第 Turn 回合（玩家走之前）自动生效的规则，RuleId 见 RuleCatalog.Create。</summary>
    [Serializable]
    public struct ScheduledRule
    {
        public int Turn;
        public string RuleId;

        public ScheduledRule(int turn, string ruleId)
        {
            Turn = turn;
            RuleId = ruleId;
        }
    }

    public sealed class GameSettings
    {
        public int Seed = Environment.TickCount;
        /// <summary>在这些回合开始时（轮到玩家走之前）进入下一个阶段：第一阶段不弹卡，第二阶段弹固定选卡。</summary>
        public int[] StageTurns = { 4, 10 };
        /// <summary>第一阶段的规则：到回合就自动生效，不用选。</summary>
        public ScheduledRule[] OpeningRules =
        {
            new ScheduledRule(5, "bishop_variants"),
            new ScheduledRule(7, "fire_hydrant"),
        };
        /// <summary>到这个回合（玩家走之前）朋友掀起风暴，进入第三阶段。</summary>
        public int StormTurn = 20;
        /// <summary>第三阶段要往岩浆里献祭几个棋子，岩浆才能站、才能过河。</summary>
        public int SacrificesNeeded = 3;
        /// <summary>第三阶段每献祭这么多个棋子，玩家选一枚棋子变成不死。0 = 不给。</summary>
        public int UndyingEvery = 2;
        /// <summary>第三阶段开始时弹一次技能，之后每隔这么多回合再弹一次。</summary>
        public int Stage3SkillEvery = 5;
        /// <summary>第三阶段开始后的第几回合朋友放技能（例如 2 = 风暴后第 2 回合）。</summary>
        public int[] FriendSkillTurns = { 2, 7 };
        /// <summary>朋友放的技能 Id，见 RuleCatalog.Create。</summary>
        public string FriendSkill = "wizard_birds";
        /// <summary>第三阶段的地形和阵型，null 时用 Stage3Layout.Default。</summary>
        public Stage3Layout Layout;
        /// <summary>超过这个回合数判平局，0 表示不限。目前只给测试用。</summary>
        public int MaxFullMoves;
    }

    /// <summary>
    /// 一整局棋：棋盘 + 阶段 + 规则 + 胜负。玩家固定执白（下方），对手是朋友（AI，执黑）。
    ///
    /// 第一阶段（第 4 回合起）：规则按 OpeningRules 的回合表自动生效，不弹卡。
    /// 第二阶段：第 10 回合固定选卡。
    /// 第一、二阶段按正式的将军规则（不能送将，王不会被吃）：玩家被将军时可以选一个救急技能；被将死就输。
    /// 进入第三阶段（风暴）：第 20 回合，或者第 10 回合选卡之后将军了对手，或者将死了对手。
    /// 第三阶段（城堡营救）：棋盘换成预设的地形和阵型，马进城堡就赢，不会输；朋友只守城堡，并按时间表放技能。
    /// 献祭完成前我方走进岩浆 = 献祭，每献祭 UndyingEvery 个选一枚棋子变成不死；献祭完成后岩浆可以站、可以过。
    ///
    /// 所有操作都会往事件队列里加事件，表现层用 DrainEvents() 取出来按顺序演出。
    /// </summary>
    public sealed class ChessGame
    {
        readonly GameSettings settings;
        readonly List<GameEvent> events = new List<GameEvent>();
        readonly List<RuleBase> activeRules = new List<RuleBase>();
        readonly RuleContext ctx;
        readonly RuleContext friendCtx;
        List<RuleBase> offers;
        OfferKind offerKind;
        IEnumerator<TargetRequest> activation;
        int nextStage;
        readonly ScheduledRule[] openingRules;
        int nextOpeningRule;
        int passesInARow;
        int lastEmergencyTurn = -1;
        int stage3StartTurn = -1;
        int nextStage3Offer;
        int friendSkillIndex;
        bool undyingPickDue;

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
        /// <summary>当前这组规则卡是在什么情况下弹出的。</summary>
        public OfferKind CurrentOfferKind => offerKind;
        /// <summary>当前这组规则卡可以跳过（救急技能和第三阶段技能可以，固定选卡不行）。</summary>
        public bool CanSkipOffers => Phase == GamePhase.ChoosingRule && offerKind != OfferKind.StageCard;
        /// <summary>ChoosingTarget 阶段等待的点选请求。</summary>
        public TargetRequest PendingTarget { get; private set; }
        /// <summary>下一次出现新规则的回合（第一阶段排好的规则，或固定选卡），没有了返回 -1。</summary>
        public int NextRuleTurn
        {
            get
            {
                if (InStage3) return -1;
                int best = nextOpeningRule < openingRules.Length ? openingRules[nextOpeningRule].Turn : -1;
                for (int stage = nextStage; stage < CardStages; stage++)
                {
                    if (RuleCatalog.CreatePool(stage).Count == 0) continue;
                    int turn = settings.StageTurns[stage];
                    if (best < 0 || turn < best) best = turn;
                    break;
                }
                return best;
            }
        }
        /// <summary>下一个阶段（0..StageTurns.Length）。</summary>
        public int NextStage => nextStage;
        public int NextStageTurn => !InStage3 && nextStage < CardStages ? settings.StageTurns[nextStage] : -1;
        public int StormTurn => settings.StormTurn;
        /// <summary>固定选卡的次数：StageTurns 里多写的回合（规则池不够的）会被忽略。</summary>
        int CardStages => Math.Min(settings.StageTurns.Length, RuleCatalog.StageCount);
        public bool InStage3 => Board.Stage3;
        public int Stage3StartTurn => stage3StartTurn;
        public GameSettings Settings => settings;

        public ChessGame(GameSettings settings = null)
        {
            this.settings = settings ?? new GameSettings();
            openingRules = (ScheduledRule[])(this.settings.OpeningRules ?? new ScheduledRule[0]).Clone();
            Array.Sort(openingRules, (a, b) => a.Turn.CompareTo(b.Turn));
            Rng = new Random(this.settings.Seed);
            Board = Board.CreateStandard();
            ctx = new RuleContext(Board, Rng, PlayerSide, events.Add);
            friendCtx = new RuleContext(Board, Rng, OpponentSide, events.Add);
            BeginTurn();
        }

        /// <summary>取出并清空目前积累的事件。</summary>
        public List<GameEvent> DrainEvents()
        {
            var list = new List<GameEvent>(events);
            events.Clear();
            return list;
        }

        /// <summary>这一方的王正被攻击。</summary>
        public bool InCheck(Side side) => MoveGenerator.InCheck(Board, side);

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

            if (result.Sacrificed)
            {
                events.Add(GameEvent.Sacrificed(mover, move.From, move.To));
                if (settings.UndyingEvery > 0 && Board.Sacrifices % settings.UndyingEvery == 0) undyingPickDue = true;
                if (result.TaskCompleted) OnSacrificeTaskCompleted();
                else events.Add(GameEvent.Dialogue(Speakers.Friend, $"The lava wants {settings.SacrificesNeeded - Board.Sacrifices} more."));
            }
            else
            {
                events.Add(GameEvent.Moved(mover, move.From, move.To));
            }
            if (result.KnockedBack != null)
            {
                events.Add(GameEvent.KnockedBack(result.KnockedBack, result.KnockFrom, result.KnockTo, mover));
                if (result.KnockedIntoLava) events.Add(GameEvent.Captured(result.KnockedBack, result.KnockTo, "lava", mover));
                if (result.Scorched)
                {
                    events.Add(GameEvent.AppearanceChanged(result.KnockedBack));
                    events.Add(GameEvent.Dialogue(Speakers.You, "Ha! Undying. It just needs a turn to cool off."));
                }
            }
            if (result.Captured != null) events.Add(GameEvent.Captured(result.Captured, move.To, "captured", mover));
            if (result.Promoted) events.Add(GameEvent.Promoted(mover));
            if (!result.Sacrificed)
                foreach (var rule in activeRules) rule.OnPieceMoved(ctx, mover, move.From);

            if (result.Rescued)
            {
                EndGame(PlayerSide, "You rescued the princess!");
                return result;
            }

            bool storm = false;
            string stormLine = null;
            // 第 10 回合选卡之后将军朋友：朋友掀起风暴。在那之前只有将死朋友才会（见 BeginTurn）
            if (!InStage3 && side == PlayerSide && nextStage >= CardStages && InCheck(OpponentSide))
            {
                storm = true;
                stormLine = "Check? Oh no you don't. Watch this!";
            }

            foreach (var rule in activeRules) rule.OnTurnEnded(ctx, side);
            if (storm) StartStorm(stormLine);
            if (undyingPickDue)
            {
                // 献祭奖励：马上让玩家点选一枚棋子变成不死，选完轮到对手
                undyingPickDue = false;
                var reward = new UndyingRule();
                if (reward.IsAvailable(ctx))
                {
                    activation = reward.Activate(ctx).GetEnumerator();
                    ContinueActivation();
                    return result;
                }
            }
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

        /// <summary>不选这一组规则卡（只有救急技能和第三阶段技能可以跳过）。</summary>
        public void SkipOffers()
        {
            if (!CanSkipOffers) throw new InvalidOperationException("These rule cards cannot be skipped");
            offers = null;
            BeginTurn();
        }

        /// <summary>回应规则的格子点选请求。传 null 表示跳过（只有允许跳过时才行）。</summary>
        public void ResolveTarget(Coord? choice)
        {
            if (Phase != GamePhase.ChoosingTarget) throw new InvalidOperationException($"No target requested during {Phase}");
            if (PendingTarget.IsPieceRequest) throw new InvalidOperationException("This request wants a piece, use ResolvePiece");
            if (choice.HasValue ? !Contains(PendingTarget.Options, choice.Value) : !PendingTarget.AllowSkip)
                throw new ArgumentException($"Invalid target {choice}");

            PendingTarget.Result = choice;
            ContinueActivation();
        }

        /// <summary>回应规则的棋子点选请求（被吃区里的棋子）。传 null 表示跳过。</summary>
        public void ResolvePiece(int? pieceId)
        {
            if (Phase != GamePhase.ChoosingTarget) throw new InvalidOperationException($"No target requested during {Phase}");
            if (!PendingTarget.IsPieceRequest) throw new InvalidOperationException("This request wants a square, use ResolveTarget");
            bool valid = pieceId.HasValue ? ContainsId(PendingTarget.PieceOptions, pieceId.Value) : PendingTarget.AllowSkip;
            if (!valid) throw new ArgumentException($"Invalid piece {pieceId}");

            PendingTarget.PieceResult = pieceId;
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
        /// 进入下一个可以行动的状态：该掀起风暴就掀；该弹规则卡就弹；朋友该放技能就放；
        /// 走子方没有合法走法就跳过；双方都动不了判平局。
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

                var side = Board.SideToMove;
                if (side == PlayerSide)
                {
                    if (!InStage3 && Board.FullMove >= settings.StormTurn)
                    {
                        StartStorm("Enough of this. Let me show you something.");
                        continue;
                    }

                    if (!InStage3 && nextStage < CardStages && Board.FullMove >= settings.StageTurns[nextStage])
                    {
                        int stage = nextStage++;
                        events.Add(GameEvent.StageStarted(stage));
                        var pool = RuleCatalog.CreatePool(stage);
                        // 规则池是空的（第一阶段）：只宣布进入新阶段，规则之后按回合表自动生效
                        if (pool.Count == 0) events.Add(GameEvent.Dialogue(Speakers.Friend, StageLine(stage)));
                        else if (Offer(OfferKind.StageCard, stage, pool, StageLine(stage))) return;
                        continue;
                    }

                    if (!InStage3 && nextOpeningRule < openingRules.Length && Board.FullMove >= openingRules[nextOpeningRule].Turn)
                    {
                        var rule = RuleCatalog.Create(openingRules[nextOpeningRule++].RuleId);
                        if (rule == null || !rule.IsAvailable(ctx)) continue;
                        activeRules.Add(rule);
                        events.Add(GameEvent.RuleActivated(rule));
                        activation = rule.Activate(ctx).GetEnumerator();
                        ContinueActivation();
                        return;
                    }

                    if (InStage3 && Board.FullMove >= nextStage3Offer)
                    {
                        nextStage3Offer = Board.FullMove + Math.Max(1, settings.Stage3SkillEvery);
                        if (Offer(OfferKind.Stage3, 2, RuleCatalog.CreateSkillPool(), "Need a hand? Pick something.")) return;
                        continue;
                    }

                    if (!InStage3 && lastEmergencyTurn != Board.FullMove && InCheck(PlayerSide))
                    {
                        lastEmergencyTurn = Board.FullMove;
                        events.Add(GameEvent.Check(PlayerSide));
                        if (Offer(OfferKind.Emergency, -1, RuleCatalog.CreateSkillPool(), "Check! ...You can use a skill, if you want.")) return;
                        continue;
                    }
                }
                else if (InStage3 && friendSkillIndex < settings.FriendSkillTurns.Length &&
                         Board.FullMove - stage3StartTurn >= settings.FriendSkillTurns[friendSkillIndex])
                {
                    friendSkillIndex++;
                    CastFriendSkill();
                }

                if (MoveGenerator.HasAnyMove(Board, side))
                {
                    Phase = side == PlayerSide ? GamePhase.PlayerTurn : GamePhase.OpponentTurn;
                    return;
                }

                // 将死（救急技能已经给过了）：玩家输；朋友被将死就掀起风暴
                if (!InStage3 && InCheck(side))
                {
                    if (side == PlayerSide)
                    {
                        EndGame(OpponentSide, "Checkmate. Your friend wins this one.");
                        return;
                    }
                    StartStorm("Checkmate?! No no no. Let's see you handle THIS.");
                    continue;
                }

                // 没有合法走法：跳过这一回合
                events.Add(GameEvent.TurnPassed(side));
                if (++passesInARow >= 2)
                {
                    EndGame(null, "Nobody can move. I think we broke it.");
                    return;
                }
                Board.Pass();
                foreach (var rule in activeRules) rule.OnTurnEnded(ctx, side);
            }
        }

        /// <summary>弹出一组规则卡。没有一张能用时返回 false。</summary>
        bool Offer(OfferKind kind, int stage, List<RuleBase> pool, string line)
        {
            pool.RemoveAll(r => !r.IsAvailable(ctx));
            if (pool.Count == 0) return false;
            offers = pool;
            offerKind = kind;
            events.Add(GameEvent.Dialogue(Speakers.Friend, line));
            events.Add(GameEvent.RulesOffered(stage, kind, offers));
            Phase = GamePhase.ChoosingRule;
            return true;
        }

        /// <summary>朋友掀起风暴：棋盘换成第三阶段的地形和阵型，轮到玩家。</summary>
        void StartStorm(string line)
        {
            events.Add(GameEvent.Dialogue(Speakers.Friend, line));
            events.Add(GameEvent.Storm());
            Board.ApplyLayout(settings.Layout ?? Stage3Layout.Default, PlayerSide, settings.SacrificesNeeded);
            nextStage = CardStages;
            stage3StartTurn = Board.FullMove;
            nextStage3Offer = Board.FullMove;
            friendSkillIndex = 0;
            passesInARow = 0;
            events.Add(GameEvent.StageStarted(2));
            events.Add(GameEvent.BoardRebuilt());
            events.Add(GameEvent.Dialogue(Speakers.Friend, "The princess is up in that castle. Only a knight can get her out."));
            events.Add(GameEvent.Dialogue(Speakers.Friend,
                $"But first, the lava. Feed it {settings.SacrificesNeeded} of your pieces and it'll let you cross."));
            if (settings.UndyingEvery > 0)
                events.Add(GameEvent.Dialogue(Speakers.Friend,
                    $"Every {settings.UndyingEvery} you feed it, one of your pieces becomes undying."));
        }

        void OnSacrificeTaskCompleted()
        {
            events.Add(GameEvent.Dialogue(Speakers.Friend, "The lava's had enough. You can walk on it now. Just don't get knocked in."));
        }

        void CastFriendSkill()
        {
            var rule = RuleCatalog.Create(settings.FriendSkill);
            if (rule == null || !rule.IsAvailable(friendCtx)) return;
            events.Add(GameEvent.OpponentSkill(rule));
            foreach (var request in rule.Activate(friendCtx))
            {
                // 朋友的技能需要点选时自动选第一个
                if (request.IsPieceRequest) request.PieceResult = request.PieceOptions.Count > 0 ? request.PieceOptions[0] : (int?)null;
                else request.Result = request.Options.Count > 0 ? request.Options[0] : (Coord?)null;
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
                case 0: return "Wait. We don't have to play like that. From now on, house rules. You'll see.";
                default: return "Your turn to change something.";
            }
        }

        static bool Contains(IReadOnlyList<Coord> list, Coord c)
        {
            if (list == null) return false;
            foreach (var item in list)
                if (item == c) return true;
            return false;
        }

        static bool ContainsId(IReadOnlyList<int> list, int id)
        {
            foreach (var item in list)
                if (item == id) return true;
            return false;
        }
    }
}
