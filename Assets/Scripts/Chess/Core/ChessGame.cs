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

    /// <summary>第三阶段的常驻技能（技能栏上的按钮）。</summary>
    public enum Stage3Skill
    {
        /// <summary>象的光束。</summary>
        Beam,
        /// <summary>马的火龙。</summary>
        Dragon,
        /// <summary>雷暴大招。</summary>
        Thunder,
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
        /// <summary>风暴后随机撒多少格岩浆。</summary>
        public int LavaSquares = 5;
        /// <summary>第三阶段开始时弹一次卡牌，之后每隔这么多回合再弹一次。</summary>
        public int Stage3SkillEvery = 5;
        /// <summary>第三阶段每次弹几张卡牌给玩家挑。</summary>
        public int Stage3CardChoices = 2;
        /// <summary>
        /// 第三阶段开始后的第几回合王后放技能（例如 2 = 风暴后第 2 回合），前一回合先预告目标格子。
        /// 列表放完后每隔 FriendSkillEvery 回合再放一次（0 = 不再放）；列表为空就从来不放。
        /// </summary>
        public int[] FriendSkillTurns = { 2 };
        public int FriendSkillEvery = 4;
        /// <summary>王后放的技能 Id，见 RuleCatalog.Create。</summary>
        public string FriendSkill = "ice_lance";
        /// <summary>象的光束、火龙用完之后要等几回合才能再用。</summary>
        public int BeamCooldown = 3;
        public int DragonCooldown = 3;
        /// <summary>第三阶段的地形，null 时用 Stage3Layout.Default。</summary>
        public Stage3Layout Layout;
        /// <summary>超过这个回合数判平局，0 表示不限。只给测试用（游戏里第三阶段没有回合上限）。</summary>
        public int MaxFullMoves;
    }

    /// <summary>
    /// 一整局棋：棋盘 + 阶段 + 规则 + 胜负。玩家固定执白（下方），对手是朋友（AI，执黑）。
    ///
    /// 第一阶段（第 4 回合起）：规则按 OpeningRules 的回合表自动生效，不弹卡。
    /// 第二阶段：第 10 回合固定选卡。
    /// 第一、二阶段按正式的将军规则（不能送将，王不会被吃）：玩家被将军时可以选一个救急技能；被将死就输。
    /// 进入第三阶段（风暴）：第 20 回合，或者第 10 回合选卡之后将军了对手，或者将死了对手。
    ///
    /// 第三阶段（攻城夺权）：
    /// - 风暴把双方的棋子吹散，朋友的国王和王后躲进城堡，再随机撒几格岩浆。双方正常吃子，王也能被吃。
    /// - 朋友城堡外的棋子都是守卫。守卫全灭后城门打开，玩家任意一枚棋子走进城堡就赢；玩家的棋子全没了就输。没有回合上限，不能复活。
    /// - 玩家的常驻技能（UseSkill）：象的光束、马的火龙（各有冷却）、雷暴大招（积累条满了放一次）。放常驻技能就是这一回合的行动。
    /// - 每隔几回合弹两张卡牌（下雨、树、山崩、自爆）：点哪张就马上用哪张，不占回合（选目标时取消就回到选卡）；
    ///   不挑就存进雷暴积累条。
    /// - 雷暴积累条：每消灭一个守卫涨一格，不挑卡涨一格，自爆多涨几格；满格数 = 开局守卫数 − 2。
    /// - 王后按时间表放技能，前一回合先在棋盘上标出目标格子。
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

        // 第三阶段
        /// <summary>正在用的卡和它所在的那组卡：选目标时取消就回到这组卡重新挑。</summary>
        RuleBase usingCard;
        List<RuleBase> usingCardOffers;
        /// <summary>正在放的常驻技能（放出去就结束这一回合）。</summary>
        Stage3Skill? usingSkill;
        int initialGuards;
        /// <summary>雷暴积累条：玩家每吃掉（打死）一枚对方棋子 +1，放完雷暴清零。</summary>
        int thunderCharge;
        /// <summary>上次结算时对方还剩几枚棋子；playerActing = 这段时间是不是玩家在行动（吃掉的算不算玩家的）。</summary>
        int enemyMark;
        bool playerActing;
        bool gateAnnounced;
        int beamReadyTurn;
        int dragonReadyTurn;
        IceLanceRule plannedFriendSkill;
        int telegraphTurn = -1;

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
        /// <summary>当前这组规则卡可以跳过（游戏逻辑上都可以；第三阶段的卡界面上不给跳过按钮）。</summary>
        public bool CanSkipOffers => Phase == GamePhase.ChoosingRule;
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

        // ---------- 第三阶段的状态（给技能栏显示） ----------

        /// <summary>第三阶段开始时的守卫数。</summary>
        public int InitialGuards => initialGuards;
        /// <summary>雷暴积累条满格数：第三阶段开始时守卫数的一半（14 个守卫 → 7）。</summary>
        public int ThunderMax => Math.Max(1, initialGuards / 2);
        /// <summary>雷暴积累条现在的格数（不超过满格）。玩家吃掉对方棋子才涨（走棋吃子、技能、卡牌都算），对方自己的回合里死的不算。</summary>
        public int ThunderCharge => Math.Min(ThunderMax, thunderCharge);
        /// <summary>城门开了（守卫全灭）。</summary>
        public bool GateOpen => Board.GateOpen;
        /// <summary>王后预告要打的格子（下一回合放技能），没有预告时为空。</summary>
        public IReadOnlyList<Coord> FriendTelegraph =>
            plannedFriendSkill?.PlannedSquares ?? (IReadOnlyList<Coord>)Array.Empty<Coord>();

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

        /// <param name="effectTargetId">
        /// 走的是消防栓或青蛙时，落地后喷谁、拉谁（玩家在预览里看到的那枚棋子的 Id）；
        /// 为 null 或者已经不是可选目标时，在同样近的目标里随机挑。
        /// </param>
        public MoveResult PlayMove(Move move, int? effectTargetId = null)
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

            if (result.CastleTaken)
            {
                EndGame(PlayerSide, "The castle is yours! Long live the new ruler.");
                return result;
            }

            ctx.PreferredTargetId = effectTargetId;
            foreach (var rule in activeRules) rule.OnPieceMoved(ctx, mover, move.From);
            ctx.PreferredTargetId = null;

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
            BeginTurn();
            return result;
        }

        public void ChooseRule(int index)
        {
            if (Phase != GamePhase.ChoosingRule) throw new InvalidOperationException($"Cannot choose a rule during {Phase}");
            if (index < 0 || index >= offers.Count) throw new ArgumentOutOfRangeException(nameof(index));

            var rule = offers[index];
            if (offerKind == OfferKind.Stage3)
            {
                // 第三阶段的卡点了就马上用，不占回合；选目标时取消就回到这组卡
                usingCardOffers = offers;
                usingCard = rule;
                offers = null;
                events.Add(GameEvent.RuleActivated(rule));
                StartActivation(rule);
                return;
            }
            offers = null;
            activeRules.Add(rule);
            events.Add(GameEvent.RuleActivated(rule));
            StartActivation(rule);
        }

        /// <summary>不选这一组规则卡。</summary>
        public void SkipOffers()
        {
            if (!CanSkipOffers) throw new InvalidOperationException("These rule cards cannot be skipped");
            offers = null;
            BeginTurn();
        }

        // ---------- 第三阶段：技能栏 ----------

        /// <summary>常驻技能还要等几回合才能用（0 = 可以用）。雷暴没有冷却，看积累条。</summary>
        public int Cooldown(Stage3Skill skill)
        {
            switch (skill)
            {
                case Stage3Skill.Beam: return Math.Max(0, beamReadyTurn - Board.FullMove);
                case Stage3Skill.Dragon: return Math.Max(0, dragonReadyTurn - Board.FullMove);
                default: return 0;
            }
        }

        /// <summary>现在（自己的回合）能不能放这个常驻技能：冷却好了、有能放的棋子和目标；雷暴还要积累条满。</summary>
        public bool CanUseSkill(Stage3Skill skill)
        {
            if (!InStage3 || Phase != GamePhase.PlayerTurn || Cooldown(skill) != 0) return false;
            if (skill == Stage3Skill.Thunder && ThunderCharge < ThunderMax) return false;
            return CreateSkill(skill).IsAvailable(ctx);
        }

        /// <summary>棋子身上绑的第三阶段技能：象 → 光束，马 → 火龙；别的棋子没有。</summary>
        public static Stage3Skill? PieceSkill(Piece piece)
        {
            if (piece == null) return null;
            if (piece.Type == PieceType.Bishop) return Stage3Skill.Beam;
            if (piece.Type == PieceType.Knight) return Stage3Skill.Dragon;
            return null;
        }

        /// <summary>这枚棋子现在能不能放它身上的技能（caster 为 null 时同 CanUseSkill(skill)）。</summary>
        public bool CanUseSkill(Stage3Skill skill, Piece caster)
        {
            if (caster == null) return CanUseSkill(skill);
            if (caster.Side != PlayerSide || !caster.OnBoard || PieceSkill(caster) != skill) return false;
            ctx.Caster = caster.Id;
            try { return CanUseSkill(skill); }
            finally { ctx.Caster = null; }
        }

        /// <summary>
        /// 放常驻技能。可能要点选目标；放出去就结束这一回合，点选时跳过就当没放。
        /// caster：点的是哪枚棋子身上的技能（象、马），给了就不用再选棋子。
        /// </summary>
        public void UseSkill(Stage3Skill skill, Piece caster = null)
        {
            if (!CanUseSkill(skill, caster)) throw new InvalidOperationException($"{skill} is not ready");
            var rule = CreateSkill(skill);
            usingSkill = skill;
            ctx.Caster = caster?.Id;
            events.Add(GameEvent.RuleActivated(rule));
            StartActivation(rule);
        }

        static RuleBase CreateSkill(Stage3Skill skill)
        {
            switch (skill)
            {
                case Stage3Skill.Beam: return new BishopBeamSkill();
                case Stage3Skill.Dragon: return new DragonKnightRule();
                default: return new ThunderSkill();
            }
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

        void StartActivation(RuleBase rule)
        {
            ctx.Acted = false;
            activation = rule.Activate(ctx).GetEnumerator();
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
            FinishActivation();
        }

        /// <summary>规则（技能）放完了：手牌放出去才拿掉；常驻技能放出去就开始冷却、结束这一回合。</summary>
        void FinishActivation()
        {
            bool acted = ctx.Acted;
            ctx.Acted = false;
            ctx.Caster = null;

            if (usingCard != null)
            {
                var cardOffers = usingCardOffers;
                usingCard = null;
                usingCardOffers = null;
                if (!acted && cardOffers != null)
                {
                    // 选目标时取消了：卡没用掉，回到这组卡重新挑（或者不挑，存进雷暴）
                    offers = cardOffers;
                    offerKind = OfferKind.Stage3;
                    events.Add(GameEvent.RulesOffered(2, OfferKind.Stage3, offers));
                    Phase = GamePhase.ChoosingRule;
                    return;
                }
            }

            if (usingSkill.HasValue)
            {
                var skill = usingSkill.Value;
                usingSkill = null;
                if (acted)
                {
                    switch (skill)
                    {
                        case Stage3Skill.Beam: beamReadyTurn = Board.FullMove + settings.BeamCooldown + 1; break;
                        case Stage3Skill.Dragon: dragonReadyTurn = Board.FullMove + settings.DragonCooldown + 1; break;
                        default:
                            // 放完雷暴积累条清零，雷暴自己打死的不算（重新开始攒）
                            thunderCharge = 0;
                            enemyMark = EnemyCount();
                            break;
                    }
                    EndTurnBySkill();
                    return;
                }
            }
            BeginTurn();
        }

        /// <summary>放常驻技能占掉这一回合：和走完一步一样结束回合，轮到对方。</summary>
        void EndTurnBySkill()
        {
            var side = Board.SideToMove;
            passesInARow = 0;
            foreach (var rule in activeRules) rule.OnTurnEnded(ctx, side);
            Board.Pass();
            BeginTurn();
        }

        /// <summary>
        /// 进入下一个可以行动的状态：该掀起风暴就掀；该弹规则卡就弹；王后该预告、该放技能就放；
        /// 第三阶段判输赢、开城门；走子方没有合法走法就跳过；双方都动不了判平局。
        /// </summary>
        void BeginTurn()
        {
            while (true)
            {
                TallyThunderCharge();
                if (settings.MaxFullMoves > 0 && Board.FullMove > settings.MaxFullMoves)
                {
                    EndGame(null, "Turn limit reached.");
                    return;
                }

                if (InStage3)
                {
                    if (!HasPieces(PlayerSide))
                    {
                        EndGame(OpponentSide, "All your pieces are gone. The castle stays shut.");
                        return;
                    }
                    if (!gateAnnounced && Board.GateOpen)
                    {
                        gateAnnounced = true;
                        plannedFriendSkill = null;
                        events.Add(GameEvent.GateOpened());
                        events.Add(GameEvent.Dialogue(Speakers.Friend, "No guards left... Fine. The gate is open."));
                        events.Add(GameEvent.Dialogue(Speakers.Friend, "Walk any piece into the castle and it's yours."));
                    }
                }

                var side = Board.SideToMove;
                playerActing = side == PlayerSide;
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
                        StartActivation(rule);
                        return;
                    }

                    if (InStage3 && !Board.GateOpen && Board.FullMove >= nextStage3Offer)
                    {
                        nextStage3Offer = Board.FullMove + Math.Max(1, settings.Stage3SkillEvery);
                        if (Offer(OfferKind.Stage3, 2, Stage3Cards(), "Need a hand? Pick a card.")) return;
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
                else if (InStage3 && !Board.GateOpen)
                {
                    UpdateFriendSkill();
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

                // 第三阶段玩家动不了但手里有能用的牌、能放的技能：还是轮到玩家
                if (InStage3 && side == PlayerSide && HasStage3Action())
                {
                    Phase = GamePhase.PlayerTurn;
                    return;
                }

                // 没有合法走法：跳过这一回合（城门开了以后朋友只剩城堡里的国王、王后，每回合都跳过，不算卡死）
                if (!(InStage3 && side == OpponentSide && Board.GateOpen))
                {
                    events.Add(GameEvent.TurnPassed(side));
                    if (++passesInARow >= 2)
                    {
                        EndGame(null, "Nobody can move. I think we broke it.");
                        return;
                    }
                }
                Board.Pass();
                foreach (var rule in activeRules) rule.OnTurnEnded(ctx, side);
            }
        }

        /// <summary>
        /// 雷暴积累条结算：从上次结算到现在对方少了几枚棋子，这段时间是玩家在行动（玩家走棋、放技能、用卡）就加进积累条。
        /// 每次轮到某一方行动前（BeginTurn）结算一次，所以用卡打死敌人后同一回合就能看到涨了。
        /// </summary>
        void TallyThunderCharge()
        {
            if (!InStage3) return;
            int now = EnemyCount();
            if (playerActing && now < enemyMark) thunderCharge = Math.Min(ThunderMax, thunderCharge + enemyMark - now);
            enemyMark = now;
        }

        int EnemyCount()
        {
            int count = 0;
            foreach (var _ in Board.PiecesOf(OpponentSide)) count++;
            return count;
        }

        bool HasPieces(Side side)
        {
            foreach (var _ in Board.PiecesOf(side)) return true;
            return false;
        }

        bool HasStage3Action()
        {
            var phase = Phase;
            Phase = GamePhase.PlayerTurn; // CanUseSkill 只在玩家回合里判断
            bool any = CanUseSkill(Stage3Skill.Beam) || CanUseSkill(Stage3Skill.Dragon) || CanUseSkill(Stage3Skill.Thunder);
            Phase = phase;
            return any;
        }

        /// <summary>第三阶段的卡牌池里随机抽 Stage3CardChoices 张（能用的）。</summary>
        List<RuleBase> Stage3Cards()
        {
            var pool = RuleCatalog.CreateStage3CardPool();
            pool.RemoveAll(r => !r.IsAvailable(ctx));
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            int count = Math.Max(1, settings.Stage3CardChoices);
            if (pool.Count > count) pool.RemoveRange(count, pool.Count - count);
            return pool;
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

        /// <summary>朋友掀起风暴：铺上第三阶段的地形，国王和王后躲进城堡，其余棋子被吹散，撒上岩浆，轮到玩家。</summary>
        void StartStorm(string line)
        {
            events.Add(GameEvent.Dialogue(Speakers.Friend, line));
            events.Add(GameEvent.Storm());
            Board.BeginStage3(settings.Layout ?? Stage3Layout.Default, PlayerSide);
            Board.Scatter(Rng, settings.LavaSquares);
            nextStage = CardStages;
            stage3StartTurn = Board.FullMove;
            nextStage3Offer = Board.FullMove;
            friendSkillIndex = 0;
            plannedFriendSkill = null;
            telegraphTurn = -1;
            passesInARow = 0;
            initialGuards = Board.GuardCount;
            thunderCharge = 0;
            enemyMark = EnemyCount();
            playerActing = false;
            gateAnnounced = false;
            usingCard = null;
            usingCardOffers = null;
            beamReadyTurn = dragonReadyTurn = 0;
            events.Add(GameEvent.StageStarted(2));
            events.Add(GameEvent.BoardRebuilt());
            events.Add(GameEvent.Dialogue(Speakers.Friend, "My king and queen are safe in the castle. Get rid of every last guard if you want in."));
            events.Add(GameEvent.Dialogue(Speakers.Friend, "Mind the lava. Whatever falls in is gone. And no coming back this time."));
            events.Add(GameEvent.Dialogue(Speakers.Friend, "Oh, and the storm got into your bishops and knights. Check your skills."));
        }

        /// <summary>
        /// 王后的技能：到了放技能的前一回合先选好目标格子、标在棋盘上；下一个朋友回合真正放出去。
        /// 预告时没有目标（例如玩家的棋子都冻住了）就跳过这一次。
        /// </summary>
        void UpdateFriendSkill()
        {
            if (plannedFriendSkill != null)
            {
                if (Board.FullMove <= telegraphTurn) return;
                var rule = plannedFriendSkill;
                plannedFriendSkill = null;
                friendSkillIndex++;
                events.Add(GameEvent.OpponentSkill(rule));
                foreach (var request in rule.Activate(friendCtx))
                {
                    // 朋友的技能需要点选时自动选第一个
                    if (request.IsPieceRequest) request.PieceResult = request.PieceOptions.Count > 0 ? request.PieceOptions[0] : (int?)null;
                    else request.Result = request.Options.Count > 0 ? request.Options[0] : (Coord?)null;
                }
                return;
            }

            int castTurn = NextFriendCastTurn();
            if (castTurn < 0 || Board.FullMove < castTurn - 1) return;
            var skill = RuleCatalog.Create(settings.FriendSkill) as IceLanceRule;
            if (skill == null || !skill.IsAvailable(friendCtx))
            {
                friendSkillIndex++;
                return;
            }
            var squares = skill.Plan(friendCtx);
            plannedFriendSkill = skill;
            telegraphTurn = Board.FullMove;
            events.Add(GameEvent.SkillTelegraphed(skill, squares));
            events.Add(GameEvent.Dialogue(Speakers.Friend, "My queen is aiming her ice lances. Next turn they hit the marked squares!"));
        }

        /// <summary>下一次王后放技能的回合（绝对回合数），不再放了返回 -1。</summary>
        int NextFriendCastTurn()
        {
            var turns = settings.FriendSkillTurns ?? new int[0];
            if (turns.Length == 0) return -1;
            if (friendSkillIndex < turns.Length) return stage3StartTurn + turns[friendSkillIndex];
            if (settings.FriendSkillEvery <= 0) return -1;
            return stage3StartTurn + turns[turns.Length - 1] + settings.FriendSkillEvery * (friendSkillIndex - turns.Length + 1);
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
