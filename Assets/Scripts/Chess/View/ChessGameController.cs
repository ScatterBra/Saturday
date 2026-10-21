using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaturdayGame.Minigames;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋盘小游戏的总控：持有一局 ChessGame，把玩家点击翻译成走子 / 选卡 / 点选目标，
    /// 让 AI 替朋友走棋，并把逻辑层的事件按顺序演出来。
    /// 棋盘、棋子、UI 都在运行时生成，场景里只需要这个组件和一台相机。
    /// </summary>
    public class ChessGameController : MonoBehaviour
    {
        [Header("Game")]
        [Tooltip("随机种子，0 表示每局随机")]
        public int seed;
        [Tooltip("在这些回合开始时进入第一、二阶段：第一阶段不弹卡，第二阶段弹固定选卡")]
        public int[] stageTurns = { 4, 10 };
        [Tooltip("第一阶段的规则：到回合自动生效。Rule Id：bishop_variants（主教四变）、fire_hydrant（消防栓）")]
        public ScheduledRule[] openingRules =
        {
            new ScheduledRule(5, "bishop_variants"),
            new ScheduledRule(7, "fire_hydrant"),
        };
        [Tooltip("到这个回合朋友掀起风暴，进入第三阶段")]
        public int stormTurn = 20;
        [Tooltip("第三阶段要往岩浆里献祭几个棋子，岩浆才能站、才能过河（马才能进城堡）")]
        public int sacrificesNeeded = 3;
        [Tooltip("第三阶段每献祭这么多个棋子，玩家选一枚棋子变成不死；0 = 不给")]
        public int undyingEvery = 2;
        [Tooltip("第三阶段每隔几回合弹一次技能")]
        public int stage3SkillEvery = 5;
        [Tooltip("第三阶段开始后的第几回合朋友放技能（鸟群）")]
        public int[] friendSkillTurns = { 2, 7 };
        [Tooltip("第三阶段的地形和阵型，留空用默认布局")]
        public Stage3LayoutAsset stage3Layout;
        [Range(0f, 1f)] public float aiBlunderChance = 0.2f;

        [Header("Presentation")]
        public Camera boardCamera;
        [Tooltip("占位用的不受光照材质（URP Unlit），留空则运行时创建")]
        public Material placeholderMaterial;
        [Tooltip("棋子贴图配置，留空则用占位图形")]
        public PieceArt pieceArt;
        [Tooltip("打开后 3D 模型（例如树）投影子，桌面和棋盘格子改用受光照的材质来接影子（颜色会随灯光明暗变化）")]
        public bool shadows = true;
        [Tooltip("打开阴影时桌面和棋盘格子用的受光照材质（URP Lit），留空则运行时创建")]
        public Material surfaceMaterial;
        public float aiThinkTime = 0.6f;
        public float moveDuration = 0.25f;
        public float effectDuration = 0.35f;
        public float dialogueDuration = 1.4f;
        [Tooltip("风暴沙尘卷起和散去各用多久")]
        public float stormDuration = 0.8f;
        [Tooltip("选了树技能后相机转到这个俯仰角，才看得出立体的树；0 = 不转")]
        [Range(0f, 90f)] public float treeCameraPitch = 65f;
        [Tooltip("风暴后（第三阶段）相机转到这个俯仰角，看得出高原和城堡的高度；0 = 不转")]
        [Range(0f, 90f)] public float stage3CameraPitch = 65f;
        [Tooltip("相机转过去用多久")]
        public float cameraTurnDuration = 1f;

        ChessGame game;
        ChessAI ai;
        BoardView board;
        ChessHud hud;
        DeskCameraFit cameraFit;
        float startPitch;
        bool busy;
        Piece selected;
        List<Move> selectedMoves = new List<Move>();
        Move? lastMove;

        public ChessGame Game => game;
        public bool IsBusy => busy;

        void Start()
        {
            EnsureEventSystem();
            if (boardCamera == null) boardCamera = GetComponentInChildren<Camera>();
            cameraFit = boardCamera != null ? boardCamera.GetComponent<DeskCameraFit>() : null;
            if (cameraFit != null) startPitch = cameraFit.pitch;
            EnsureLight();
            var factory = new ChessViewFactory(placeholderMaterial, boardCamera, pieceArt, surfaceMaterial, shadows);
            board = new BoardView(transform, factory);
            hud = ChessHud.Create(transform, boardCamera, pieceArt);
            hud.RuleCardClicked += ChooseRule;
            hud.SkipClicked += Skip;
            hud.LostPieceClicked += id => ResolvePiece(id);
            hud.PlayAgainClicked += NewGame;
            hud.BackClicked += () => MinigameSession.Finish(game.Winner == game.PlayerSide ? "won" : game.Winner == null ? "draw" : "lost");
            NewGame();
        }

        public void NewGame()
        {
            StopAllCoroutines();
            busy = false;
            var settings = new GameSettings
            {
                StageTurns = stageTurns,
                OpeningRules = openingRules,
                StormTurn = stormTurn,
                SacrificesNeeded = sacrificesNeeded,
                UndyingEvery = undyingEvery,
                Stage3SkillEvery = stage3SkillEvery,
                FriendSkillTurns = friendSkillTurns,
                Layout = stage3Layout != null ? stage3Layout.Build() : null,
            };
            if (seed != 0) settings.Seed = seed;
            game = new ChessGame(settings);
            ai = new ChessAI(new System.Random(settings.Seed + 1)) { BlunderChance = aiBlunderChance };
            selected = null;
            selectedMoves.Clear();
            lastMove = null;
            board.Bind(game.Board);
            hud.ResetForNewGame();
            hud.SetStorm(0);
            // 上一局转过的相机回到场景里设的角度
            if (cameraFit != null)
            {
                cameraFit.pitch = startPitch;
                cameraFit.extraPoints.Clear();
                cameraFit.Fit();
            }
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        void Update()
        {
            if (busy || game == null) return;
            if (game.Phase == GamePhase.OpponentTurn)
            {
                StartCoroutine(OpponentTurn());
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame) Deselect();
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (board.TryGetCoord(boardCamera.ScreenPointToRay(mouse.position.ReadValue()), out var coord)) ClickSquare(coord);
        }

        // ---------- 玩家操作（也方便在编辑器里用代码驱动测试） ----------

        public void ClickSquare(Coord coord)
        {
            if (busy) return;
            if (game.Phase == GamePhase.ChoosingTarget)
            {
                if (game.PendingTarget.IsPieceRequest) return;
                if (game.PendingTarget.Options.Contains(coord)) ResolveTarget(coord);
                return;
            }
            if (game.Phase != GamePhase.PlayerTurn) return;

            if (selected != null)
            {
                foreach (var move in selectedMoves)
                {
                    if (move.To != coord) continue;
                    PlayMove(move);
                    return;
                }
            }

            var piece = game.Board.PieceAt(coord);
            if (piece != null && piece.Side == game.PlayerSide) Select(piece);
            else Deselect();
        }

        public void ChooseRule(int index)
        {
            if (busy || game.Phase != GamePhase.ChoosingRule) return;
            hud.ShowSkip(false);
            game.ChooseRule(index);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        /// <summary>Skip 按钮：跳过可选的技能卡，或跳过可选的点选。</summary>
        public void Skip()
        {
            if (busy) return;
            if (game.Phase == GamePhase.ChoosingRule && game.CanSkipOffers)
            {
                hud.ShowSkip(false);
                hud.HideOffers();
                game.SkipOffers();
                StartCoroutine(PlayEvents(game.DrainEvents()));
            }
            else if (game.Phase == GamePhase.ChoosingTarget && game.PendingTarget.AllowSkip)
            {
                if (game.PendingTarget.IsPieceRequest) ResolvePiece(null);
                else ResolveTarget(null);
            }
        }

        public void ResolveTarget(Coord? coord)
        {
            if (busy || game.Phase != GamePhase.ChoosingTarget || game.PendingTarget.IsPieceRequest) return;
            hud.ShowSkip(false);
            game.ResolveTarget(coord);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        /// <summary>在规则书下方的被吃区里点选了一枚棋子（复活技能）。</summary>
        public void ResolvePiece(int? pieceId)
        {
            if (busy || game.Phase != GamePhase.ChoosingTarget || !game.PendingTarget.IsPieceRequest) return;
            hud.ShowSkip(false);
            game.ResolvePiece(pieceId);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        void PlayMove(Move move)
        {
            Deselect();
            lastMove = move;
            game.PlayMove(move);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        void Select(Piece piece)
        {
            selected = piece;
            selectedMoves = game.MovesFor(piece);
            board.Highlight(piece.Pos, selectedMoves, null, lastMove, DangerSquare());
            if (piece.IsFrozen)
                hud.SetStatus($"That {Name(piece)} is frozen.");
            else if (piece.Undying)
                hud.SetStatus($"That {Name(piece)} is undying: lava only stops it for a turn.");
            else
                UpdateStatus();
        }

        void Deselect()
        {
            selected = null;
            selectedMoves.Clear();
            if (game.Phase != GamePhase.ChoosingTarget) board.Highlight(null, null, null, lastMove, DangerSquare());
            UpdateStatus();
        }

        /// <summary>玩家被将军时王所在的格子（标红）。</summary>
        Coord? DangerSquare()
        {
            if (game.Phase == GamePhase.GameOver || game.InStage3 || !game.InCheck(game.PlayerSide)) return null;
            return game.Board.King(game.PlayerSide)?.Pos;
        }

        IEnumerator OpponentTurn()
        {
            busy = true;
            UpdateStatus();
            yield return new WaitForSeconds(aiThinkTime);
            var move = ai.ChooseMove(game.Board);
            if (move.HasValue)
            {
                lastMove = move;
                game.PlayMove(move.Value);
            }
            yield return PlayEvents(game.DrainEvents());
        }

        // ---------- 演出 ----------

        IEnumerator PlayEvents(List<GameEvent> events)
        {
            busy = true;
            board.Highlight(null, null, null, lastMove);
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case GameEventType.Moved:
                        yield return board.AnimateMove(e.PieceId, e.From, e.To, moveDuration);
                        break;
                    case GameEventType.Sacrificed:
                        yield return board.AnimateSacrifice(e.PieceId, e.To, moveDuration);
                        break;
                    case GameEventType.Pushed:
                        yield return board.AnimatePush(e.PieceId, e.To, effectDuration * 1.5f);
                        break;
                    case GameEventType.KnockedBack:
                        yield return board.AnimateKnockback(e.PieceId, e.To, effectDuration * 2);
                        break;
                    case GameEventType.Captured:
                        yield return board.AnimateCapture(e.PieceId, e.Text == "burned" || e.Text == "lava", effectDuration);
                        break;
                    case GameEventType.Promoted:
                    case GameEventType.AppearanceChanged:
                        yield return board.AnimatePop(e.PieceId, effectDuration);
                        break;
                    case GameEventType.Revived:
                        yield return board.AnimateSpawn(e.PieceId, effectDuration * 2);
                        break;
                    case GameEventType.LavaAdded:
                        board.Highlight(null, null, null, lastMove);
                        yield return new WaitForSeconds(effectDuration);
                        break;
                    case GameEventType.Storm:
                        // 沙尘卷起，盖住整个画面；棋盘在 BoardRebuilt 时换掉
                        for (float t = 0; t < 1f; t += Time.deltaTime / stormDuration)
                        {
                            hud.SetStorm(t);
                            yield return null;
                        }
                        hud.SetStorm(1);
                        break;
                    case GameEventType.BoardRebuilt:
                        lastMove = null;
                        selected = null;
                        board.RebuildTerrain();
                        board.SyncAll();
                        board.Highlight(null, null, null, null);
                        // 沙尘还盖着画面时把相机转到斜视角，并把升起来的城堡框进画面
                        if (cameraFit != null)
                        {
                            cameraFit.extraPoints = board.TallPoints();
                            if (stage3CameraPitch > 0f && !Mathf.Approximately(cameraFit.pitch, stage3CameraPitch))
                                yield return cameraFit.TurnTo(stage3CameraPitch, cameraTurnDuration);
                            else
                                cameraFit.Fit();
                        }
                        yield return new WaitForSeconds(0.3f);
                        for (float t = 1f; t > 0f; t -= Time.deltaTime / stormDuration)
                        {
                            hud.SetStorm(t);
                            yield return null;
                        }
                        hud.SetStorm(0);
                        break;
                    case GameEventType.OpponentSkill:
                        hud.ShowCaption(Speakers.Friend, $"*casts {e.Rule.Title}*");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.TurnPassed:
                        hud.ShowCaption(null, e.Side == game.PlayerSide ? "You can't move. Turn skipped." : "Your friend can't move. Turn skipped.");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.RulesOffered:
                        hud.ShowOffers(e.Rules, e.OfferKind);
                        hud.ShowSkip(e.OfferKind != OfferKind.StageCard);
                        break;
                    case GameEventType.RuleActivated:
                        hud.HideOffers();
                        hud.ShowSkip(false);
                        hud.AddRule(e.Rule);
                        if (e.Rule is TreePawnsRule && cameraFit != null && treeCameraPitch > 0f && !Mathf.Approximately(cameraFit.pitch, treeCameraPitch))
                            yield return cameraFit.TurnTo(treeCameraPitch, cameraTurnDuration);
                        break;
                    case GameEventType.TargetRequested:
                        if (!e.Request.IsPieceRequest) board.Highlight(null, null, e.Request.Options, lastMove);
                        hud.ShowSkip(e.Request.AllowSkip);
                        break;
                    case GameEventType.Dialogue:
                        hud.ShowCaption(e.Speaker, e.Text);
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.GameOver:
                        hud.ShowGameOver(e.Text, MinigameSession.IsHosted);
                        break;
                }
            }
            // 对白播完就收起字幕，不挡住棋盘
            hud.HideCaption();
            board.SyncAll();
            bool pickingPiece = game.Phase == GamePhase.ChoosingTarget && game.PendingTarget.IsPieceRequest;
            hud.SetLostPieces(game.Board.CapturedOf(game.PlayerSide), pickingPiece ? game.PendingTarget.PieceOptions.ToList() : null);
            if (game.Phase == GamePhase.ChoosingTarget && !pickingPiece)
                board.Highlight(null, null, game.PendingTarget.Options, lastMove);
            else
                board.Highlight(null, null, null, lastMove, DangerSquare());
            UpdateStatus();
            busy = false;
        }

        void UpdateStatus()
        {
            int turn = game.Board.FullMove;
            var b = game.Board;
            switch (game.Phase)
            {
                case GamePhase.PlayerTurn:
                    if (game.InStage3 && !b.TaskComplete)
                        hud.SetStatus($"Turn {turn} - feed the lava ({b.Sacrifices}/{b.SacrificesNeeded}) or move");
                    else if (game.InStage3)
                        hud.SetStatus($"Turn {turn} - get a knight into the castle!");
                    else if (game.InCheck(game.PlayerSide))
                        hud.SetStatus($"Turn {turn} - Check! Your move");
                    else
                        hud.SetStatus($"Turn {turn} - your move");
                    break;
                case GamePhase.OpponentTurn:
                    hud.SetStatus($"Turn {turn} - your friend is thinking...");
                    break;
                case GamePhase.ChoosingRule:
                    hud.SetStatus(game.CurrentOfferKind == OfferKind.StageCard
                        ? $"Turn {turn} - pick a new house rule"
                        : $"Turn {turn} - pick a skill, or skip");
                    break;
                case GamePhase.ChoosingTarget:
                    hud.SetStatus(game.PendingTarget.Prompt);
                    break;
                case GamePhase.GameOver:
                    hud.SetStatus("Game over");
                    break;
            }

            if (game.Phase != GamePhase.ChoosingRule)
            {
                string hint;
                if (game.InStage3) hint = "Castle rescue! Skills show up every few turns.";
                else if (game.NextRuleTurn > 0) hint = $"A new rule appears on turn {game.NextRuleTurn}.";
                else hint = "No more new rules... but something is brewing.";
                hud.SetNewHint(hint);
            }
        }

        static string Name(Piece piece) => piece.Type.ToString().ToLowerInvariant();

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
        }

        /// <summary>3D 模型（例如树）用的是受光照的材质，场景里没有灯时补一盏平行光，不然模型是黑的。占位图形和贴图不受光照影响。</summary>
        void EnsureLight()
        {
            if (FindAnyObjectByType<Light>() != null) return;
            var go = new GameObject("Model Light");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
        }
    }
}
