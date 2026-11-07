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
        [Tooltip("第一阶段的规则：到回合自动生效。Rule Id：bishop_variants（主教四变）、fire_hydrant（a 列车变消防栓、h 列变青蛙，朋友的反过来）")]
        public ScheduledRule[] openingRules =
        {
            new ScheduledRule(5, "bishop_variants"),
            new ScheduledRule(7, "fire_hydrant"),
        };
        [Tooltip("到这个回合朋友掀起风暴，进入第三阶段")]
        public int stormTurn = 20;
        [Tooltip("风暴后随机撒多少格岩浆")]
        public int lavaSquares = 5;
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
        [Tooltip("场景里摆好的桌面（Chess/Desk：背景、棋盘图、NEW 区、规则书、规则卡底图），留空时自动在子物体里找；找不到就用占位图形")]
        public ChessDesk desk;
        [Tooltip("音效（Chess 物体上的 ChessSounds），留空时自动在本物体上找；找不到就没有声音")]
        public ChessSounds sounds;
        [Tooltip("标题字体（NEW、Rule Book、卡片标题、状态栏、说话人、结束文字、按钮、棋子上的小字），留空用 Unity 默认字体加粗")]
        public Font titleFont;
        [Tooltip("正文字体（规则描述、规则书、提示、对白），留空用 Unity 默认字体")]
        public Font bodyFont;
        [Tooltip("打开后 3D 模型（例如树）投影子，桌面和棋盘格子改用受光照的材质来接影子（颜色会随灯光明暗变化）")]
        public bool shadows = true;
        [Tooltip("打开阴影时桌面和棋盘格子用的受光照材质（URP Lit），留空则运行时创建")]
        public Material surfaceMaterial;
        public float aiThinkTime = 0.6f;
        public float moveDuration = 0.25f;
        public float effectDuration = 0.35f;
        [Tooltip("朋友放技能、跳过回合这类提示停留多久；对白不受影响，要点一下才继续")]
        public float dialogueDuration = 1.4f;
        [Tooltip("规则卡出现时，每张卡从左边飞进来用多久（一张接一张）")]
        public float cardFlyDuration = 0.3f;
        [Tooltip("风暴沙尘卷起和散去各用多久")]
        public float stormDuration = 0.8f;
        [Tooltip("中间视角：第三阶段之前，棋盘上第一次出现 3D 模型（第 5 回合主教变体，或者树）时相机转到这个俯仰角；0 = 不转。Play 模式下斜过来之后改它会实时生效")]
        [Range(0f, 90f)] public float midCameraPitch = 60f;
        [Tooltip("中间视角的视野角")]
        [Range(10f, 100f)] public float midFieldOfView = 30f;
        [Tooltip("中间视角的缩放，1 = 刚好框住整个布局")]
        [Range(0.5f, 3f)] public float midZoom = 1f;
        [Tooltip("风暴后（第三阶段）相机转到这个俯仰角；0 = 不转")]
        [Range(0f, 90f)] public float stage3CameraPitch = 65f;
        [Tooltip("第三阶段相机的视野角。越大近大远小越明显，山显得越高；Play 模式下在第三阶段改它会实时生效")]
        [Range(10f, 100f)] public float stage3FieldOfView = 30f;
        [Tooltip("第三阶段的缩放，1 = 刚好框住整个布局；大于 1 拉近，小于 1 拉远；Play 模式下在第三阶段改它会实时生效")]
        [Range(0.5f, 3f)] public float stage3Zoom = 1f;
        [Tooltip("相机转过去用多久")]
        public float cameraTurnDuration = 1f;
        [Tooltip("右键拖动转视角的灵敏度（每个像素转多少度）")]
        public float orbitSensitivity = 0.25f;
        [Tooltip("滚轮每一格拉近、拉远多少")]
        public float orbitZoomStep = 0.1f;
        [Tooltip("要看规则卡、点阵亡区时，相机自动转回正面用多久")]
        public float returnToFrontDuration = 0.4f;
        [Tooltip("鼠标在有特殊外观或状态的棋子上停多久才弹出说明（秒）")]
        public float tooltipDelay = 0.2f;

        ChessGame game;
        ChessAI ai;
        BoardView board;
        ChessHud hud;
        DeskCameraFit cameraFit;
        float startPitch;
        float startFieldOfView;
        float startZoom;
        bool cameraTurning;
        /// <summary>这一局相机已经转到中间视角了（只转一次）。</summary>
        bool midViewShown;
        /// <summary>可以右键拖动转视角了（中间视角出现后、第三阶段）。</summary>
        bool orbitUnlocked;
        Vector2 rightPressPosition;
        bool rightDragging;
        float lastRightClickTime = -10f;
        const float DragThreshold = 6f;
        bool busy;
        bool jumpToStage3;
        Piece selected;
        List<Move> selectedMoves = new List<Move>();
        /// <summary>选中消防栓或青蛙时，鼠标停在哪个落点上，以及落下去会喷谁、拉谁（玩家走这一步就照这个来）。</summary>
        Coord? previewSquare;
        HydrantSpray.Result? preview;
        Move? lastMove;
        /// <summary>鼠标停在哪枚有说明的棋子上（-1 = 没有），从什么时候开始停的。</summary>
        int hoverPieceId = -1;
        float hoverSince;

        public ChessGame Game => game;
        public bool IsBusy => busy;

        void Start()
        {
            EnsureEventSystem();
            if (sounds == null) sounds = GetComponent<ChessSounds>();
            if (boardCamera == null) boardCamera = GetComponentInChildren<Camera>();
            cameraFit = boardCamera != null ? boardCamera.GetComponent<DeskCameraFit>() : null;
            if (cameraFit != null)
            {
                startPitch = cameraFit.pitch;
                startFieldOfView = cameraFit.fieldOfView;
                startZoom = cameraFit.zoom;
            }
            EnsureLight();
            CollectStageLights();
            var factory = new ChessViewFactory(placeholderMaterial, boardCamera, pieceArt, surfaceMaterial, shadows, titleFont, bodyFont,
                cameraFit != null ? cameraFit.ViewReference : null);
            if (desk == null) desk = GetComponentInChildren<ChessDesk>();
            board = new BoardView(transform, factory, desk);
            hud = ChessHud.Create(transform, boardCamera, pieceArt, desk, titleFont, bodyFont);
            hud.RuleCardClicked += ChooseRule;
            hud.SkipClicked += Skip;
            hud.LostPieceClicked += id => ResolvePiece(id);
            hud.PlayAgainClicked += NewGame;
            hud.BackClicked += () => MinigameSession.Finish(game.Winner == game.PlayerSide ? "won" : game.Winner == null ? "draw" : "lost");
            NewGame();
        }

        /// <summary>调试用：开一局新棋，一开局就掀起风暴进入第三阶段（走真实的风暴流程，相机、棋子朝向都会跟着变）。</summary>
        [ContextMenu("Jump To Stage 3")]
        public void JumpToStage3()
        {
            jumpToStage3 = true;
            try { NewGame(); }
            finally { jumpToStage3 = false; }
        }

        // 中间视角、第三阶段时在 Inspector 里改相机参数，立刻套到相机上预览（相机还在转的时候不打断它）
        void OnValidate()
        {
            if (!Application.isPlaying || cameraFit == null || game == null || cameraTurning) return;
            if (game.InStage3)
            {
                if (stage3CameraPitch > 0f) cameraFit.pitch = stage3CameraPitch;
                cameraFit.fieldOfView = stage3FieldOfView;
                cameraFit.zoom = stage3Zoom;
            }
            else if (midViewShown)
            {
                if (midCameraPitch > 0f) cameraFit.pitch = midCameraPitch;
                cameraFit.fieldOfView = midFieldOfView;
                cameraFit.zoom = midZoom;
            }
            else return;
            cameraFit.Fit();
        }

        /// <summary>第三阶段之前，棋盘上第一次出现 3D 模型时，相机转到中间视角（每局一次）。</summary>
        IEnumerator TurnToMidViewIfNeeded()
        {
            if (midViewShown || game.InStage3 || cameraFit == null || !board.AnyModel) yield break;
            midViewShown = true;
            float pitch = midCameraPitch > 0f ? midCameraPitch : cameraFit.pitch;
            cameraTurning = true;
            PlaySound(s => s.cameraChange);
            yield return cameraFit.TurnTo(pitch, midFieldOfView, midZoom, cameraTurnDuration);
            cameraTurning = false;
            orbitUnlocked = true;
        }

        public void NewGame()
        {
            StopAllCoroutines();
            busy = false;
            cameraTurning = false;
            midViewShown = false;
            orbitUnlocked = false;
            rightDragging = false;
            if (cameraFit != null) cameraFit.SetOrbit(0f, 0f, 1f);
            var settings = new GameSettings
            {
                StageTurns = stageTurns,
                OpeningRules = openingRules,
                // 调试跳关：第一回合就掀起风暴
                StormTurn = jumpToStage3 ? 1 : stormTurn,
                LavaSquares = lavaSquares,
                Stage3SkillEvery = stage3SkillEvery,
                FriendSkillTurns = friendSkillTurns,
                Layout = stage3Layout != null ? stage3Layout.Build() : null,
            };
            if (seed != 0) settings.Seed = seed;
            game = new ChessGame(settings);
            ai = new ChessAI(new System.Random(settings.Seed + 1)) { BlunderChance = aiBlunderChance };
            selected = null;
            selectedMoves.Clear();
            previewSquare = null;
            preview = null;
            lastMove = null;
            board.Bind(game.Board);
            ApplyStageLighting(game.Board.Stage3);
            hud.ResetForNewGame();
            hud.SetStorm(0);
            // 上一局转过的相机回到场景里设的角度，棋子重新平躺
            FaceCamera.FollowCameraPitch = false;
            if (cameraFit != null)
            {
                cameraFit.pitch = startPitch;
                cameraFit.fieldOfView = startFieldOfView;
                cameraFit.zoom = startZoom;
                cameraFit.extraPoints.Clear();
                cameraFit.Fit();
            }
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        void Update()
        {
            if (game == null) return;
            UpdateOrbit();
            UpdateTooltip();
            if (busy) return;
            if (game.Phase == GamePhase.OpponentTurn)
            {
                StartCoroutine(OpponentTurn());
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            UpdatePreview(mouse.position.ReadValue());
            UpdateRevivePreview(mouse.position.ReadValue());
            // 右键原地单击 = 取消选中（拖动转视角在 UpdateOrbit 里）
            if (mouse.rightButton.wasReleasedThisFrame && !rightDragging) Deselect();
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (board.TryGetPointedCoord(boardCamera.ScreenPointToRay(mouse.position.ReadValue()), out var coord)) ClickSquare(coord);
        }

        /// <summary>
        /// 鼠标停在有特殊外观或状态的棋子上（消防栓、青蛙、主教变体、龙、树、被鸟屎糊住的、复活的……）一小会儿，就在旁边弹出说明。
        /// 拖动视角、鼠标在 UI 上、风暴遮着画面时不弹。演出中也能看（例如对白停着等点击时）。
        /// </summary>
        void UpdateTooltip()
        {
            var mouse = Mouse.current;
            int id = -1;
            if (mouse != null && !(mouse.rightButton.isPressed && rightDragging) && !cameraTurning &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) &&
                board.TryGetPiece(boardCamera.ScreenPointToRay(mouse.position.ReadValue()), out int hit) &&
                PieceTooltip.HasTooltip(game.Board.GetPiece(hit)))
                id = hit;
            if (id != hoverPieceId)
            {
                hoverPieceId = id;
                hoverSince = Time.unscaledTime;
            }
            var info = id >= 0 && Time.unscaledTime - hoverSince >= tooltipDelay
                ? PieceTooltip.Describe(game.Board.GetPiece(id), game.PlayerSide, game.InStage3)
                : null;
            if (info.HasValue) hud.ShowTooltip(info.Value, mouse.position.ReadValue());
            else hud.HideTooltip();
        }

        /// <summary>
        /// 右键拖动转视角（水平、俯仰），滚轮拉近拉远，双击右键转回正面。
        /// 中间视角出现后、第三阶段才能用；程序自己转相机的时候不响应。忙着演出时也能转。
        /// </summary>
        void UpdateOrbit()
        {
            var mouse = Mouse.current;
            if (mouse == null || cameraFit == null) return;
            var position = mouse.position.ReadValue();
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightPressPosition = position;
                rightDragging = false;
            }
            bool canOrbit = orbitUnlocked && !cameraTurning;
            if (mouse.rightButton.isPressed && canOrbit)
            {
                if (!rightDragging && (position - rightPressPosition).magnitude > DragThreshold) rightDragging = true;
                if (rightDragging)
                {
                    var delta = mouse.delta.ReadValue();
                    cameraFit.Drag(delta.x * orbitSensitivity, -delta.y * orbitSensitivity);
                }
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                cameraFit.EndDrag();
                if (!rightDragging && canOrbit)
                {
                    // 双击右键：转回正面
                    if (Time.unscaledTime - lastRightClickTime < 0.35f) StartCoroutine(ReturnToFront());
                    lastRightClickTime = Time.unscaledTime;
                }
                // rightDragging 留到下一次按下再清，这一帧的 Update 还要靠它判断“不是单击”
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (canOrbit && Mathf.Abs(scroll) > 0.01f)
                cameraFit.Zoom(scroll > 0 ? 1f + orbitZoomStep : 1f / (1f + orbitZoomStep));
        }

        /// <summary>转回正面视角（要看规则卡、点阵亡区时用）。</summary>
        IEnumerator ReturnToFront()
        {
            if (cameraFit == null || cameraFit.IsFront) yield break;
            cameraTurning = true;
            yield return cameraFit.ReturnToFront(returnToFrontDuration);
            cameraTurning = false;
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
                    // 预览过这个落点就照预览来；没预览过（例如用代码点）就让规则自己随机挑
                    int? effectTarget = previewSquare == coord && preview.HasValue ? preview.Value.Piece.Id : (int?)null;
                    PlayMove(move, effectTarget);
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

        void PlayMove(Move move, int? effectTarget = null)
        {
            Deselect();
            lastMove = move;
            game.PlayMove(move, effectTarget);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        void Select(Piece piece)
        {
            selected = piece;
            selectedMoves = game.MovesFor(piece);
            previewSquare = null;
            preview = null;
            board.Highlight(piece.Pos, selectedMoves, null, lastMove, DangerSquare());
            if (piece.IsFrozen)
                hud.SetStatus($"That {Name(piece)} is frozen.");
            else
                UpdateStatus();
        }

        void Deselect()
        {
            selected = null;
            selectedMoves.Clear();
            previewSquare = null;
            preview = null;
            if (game.Phase != GamePhase.ChoosingTarget) board.Highlight(null, null, null, lastMove, DangerSquare());
            UpdateStatus();
        }

        /// <summary>
        /// 选中的是消防栓或青蛙时，鼠标移到哪个落点，就在一份棋盘拷贝上走一遍，标出落地后会喷谁、拉谁、落到哪。
        /// 同样近的目标有好几个时随机挑一个显示，玩家走这一步时就用这一个。
        /// </summary>
        void UpdatePreview(Vector2 mousePosition)
        {
            Coord? hovered = null;
            if (selected != null && game.Phase == GamePhase.PlayerTurn &&
                board.TryGetPointedCoord(boardCamera.ScreenPointToRay(mousePosition), out var c) && selectedMoves.Any(m => m.To == c))
                hovered = c;
            if (hovered == previewSquare) return;

            previewSquare = hovered;
            preview = null;
            if (selected == null) return; // 选中的棋子已经没了（例如开了新局），只清掉记录
            if (hovered.HasValue)
            {
                var move = selectedMoves.First(m => m.To == hovered.Value);
                var next = game.Board.Clone();
                var result = next.Apply(move);
                var options = result.Rescued ? new List<HydrantSpray.Result>() : FireHydrantRule.EffectCandidates(next, next.GetPiece(move.PieceId));
                if (options.Count > 0) preview = options[Random.Range(0, options.Count)];
            }
            board.Highlight(selected.Pos, selectedMoves, null, lastMove, DangerSquare(),
                preview.HasValue ? (preview.Value.Piece.Pos, preview.Value.To, preview.Value.Burns) : ((Coord, Coord, bool)?)null);
        }

        /// <summary>复活时挑位置：鼠标停在哪个可选格子上，就在那里显示这枚棋子复活后的样子。</summary>
        void UpdateRevivePreview(Vector2 mousePosition)
        {
            var request = game.Phase == GamePhase.ChoosingTarget ? game.PendingTarget : null;
            if (request == null || request.IsPieceRequest || !request.PreviewPieceId.HasValue)
            {
                board.HideGhost();
                return;
            }
            Coord? hovered = null;
            if (board.TryGetCoord(boardCamera.ScreenPointToRay(mousePosition), out var c) && request.Options.Contains(c)) hovered = c;
            board.ShowGhost(game.Board.GetPiece(request.PreviewPieceId.Value), request.PreviewSkin, hovered);
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

        /// <summary>等玩家点一下鼠标（或按空格、回车）。至少等一帧，避免把触发这段演出的那一下也算进去。</summary>
        IEnumerator WaitForClick()
        {
            yield return null;
            while (true)
            {
                var mouse = Mouse.current;
                var keyboard = Keyboard.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) yield break;
                if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) yield break;
                yield return null;
            }
        }

        void PlaySound(System.Func<ChessSounds, ChessSounds.Sfx> pick)
        {
            if (sounds != null) sounds.Play(pick(sounds));
        }

        IEnumerator PlayEvents(List<GameEvent> events)
        {
            busy = true;
            board.HideGhost();
            board.Highlight(null, null, null, lastMove);
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case GameEventType.Moved:
                        yield return board.AnimateMove(e.PieceId, e.From, e.To, moveDuration);
                        PlaySound(s => s.pieceFall);
                        break;
                    case GameEventType.Pushed:
                        // 推子的是消防栓就是喷水，是青蛙就是舌头
                        var pusher = e.SourcePieceId >= 0 ? game.Board.GetPiece(e.SourcePieceId) : null;
                        if (pusher?.Skin == FireHydrantRule.Hydrant) PlaySound(s => s.hydrant);
                        else if (pusher?.Skin == FireHydrantRule.Frog) PlaySound(s => s.frog);
                        yield return board.AnimatePush(e.PieceId, e.To, effectDuration * 1.5f);
                        break;
                    case GameEventType.KnockedBack:
                        yield return board.AnimateKnockback(e.PieceId, e.To, effectDuration * 2);
                        break;
                    case GameEventType.Captured:
                        bool burned = e.Text == "burned" || e.Text == "lava";
                        if (burned) PlaySound(s => s.burn);
                        yield return board.AnimateCapture(e.PieceId, burned, effectDuration);
                        break;
                    case GameEventType.Promoted:
                        yield return board.AnimatePop(e.PieceId, effectDuration);
                        break;
                    case GameEventType.AppearanceChanged:
                        if (e.Frozen && e.Text == "tree") PlaySound(s => s.tree);
                        else if (!e.Frozen && e.Text != null) PlaySound(s => s.pieceChange);
                        yield return board.AnimatePop(e.PieceId, effectDuration);
                        // 第一个 3D 模型冒出来了（主教变体、消防栓、树……）：相机斜过来才看得出立体
                        yield return TurnToMidViewIfNeeded();
                        break;
                    case GameEventType.Revived:
                        yield return board.AnimateSpawn(e.PieceId, effectDuration * 2);
                        PlaySound(s => s.pieceFall);
                        // 复活的是消防栓、主教变体这类 3D 模型：第一次出现 3D 时转到中间视角
                        yield return TurnToMidViewIfNeeded();
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
                        previewSquare = null;
                        preview = null;
                        board.RebuildTerrain();
                        board.SyncAll();
                        // 沙尘盖着画面时换灯：平行光关掉，聚光灯打开
                        ApplyStageLighting(true);
                        board.Highlight(null, null, null, null);
                        // 第三阶段：棋子随着相机的角度立起来
                        FaceCamera.FollowCameraPitch = true;
                        hud.SetStage3(true);
                        // 沙尘还盖着画面时把相机转到斜视角，并把升起来的城堡框进画面
                        if (cameraFit != null)
                        {
                            cameraFit.extraPoints = board.TallPoints();
                            float pitch = stage3CameraPitch > 0f ? stage3CameraPitch : cameraFit.pitch;
                            cameraTurning = true;
                            // 沙尘盖着画面：玩家之前转过的视角直接回到正面，第三阶段从正面开始看
                            cameraFit.SetOrbit(0f, 0f, 1f);
                            PlaySound(s => s.cameraChange);
                            yield return cameraFit.TurnTo(pitch, stage3FieldOfView, stage3Zoom, cameraTurnDuration);
                            cameraTurning = false;
                            orbitUnlocked = true;
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
                        yield return ReturnToFront();
                        hud.ShowOffers(e.Rules, e.OfferKind);
                        // 从上到下一张一张飞进来，每张一声
                        yield return hud.FlyInCards(cardFlyDuration, () => PlaySound(s => s.paperPopup));
                        hud.ShowSkip(true);
                        break;
                    case GameEventType.RuleActivated:
                        hud.HideOffers();
                        hud.ShowSkip(false);
                        hud.AddRule(e.Rule);
                        break;
                    case GameEventType.TargetRequested:
                        if (e.Request.IsPieceRequest) yield return ReturnToFront(); // 要在规则书的阵亡区里点
                        if (!e.Request.IsPieceRequest) board.Highlight(null, null, e.Request.Options, lastMove);
                        hud.ShowSkip(e.Request.AllowSkip);
                        break;
                    case GameEventType.Dialogue:
                        hud.ShowCaption(e.Speaker, e.Text);
                        yield return WaitForClick();
                        break;
                    case GameEventType.GameOver:
                        hud.ShowGameOver(e.Text, MinigameSession.IsHosted);
                        break;
                }
                // 树被吃掉、掉进岩浆：和出现时一样播种树的音效
                if (board.ConsumeTreeVanished()) PlaySound(s => s.tree);
            }
            // 对白播完就收起字幕，不挡住棋盘
            hud.HideCaption();
            board.SyncAll();
            // 树变回兵（冻结到期）在这里刷出来；好几棵同时变回也只播一次
            if (board.ConsumeTreeVanished()) PlaySound(s => s.tree);
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
                    if (game.InStage3)
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
                        ? $"Turn {turn} - pick a new house rule, or skip"
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

        readonly List<Light> directionalLights = new List<Light>();
        readonly List<Light> spotLights = new List<Light>();

        /// <summary>记下本场景里摆好的灯（开局时就在的，特效自带的灯不算），按类型分开。</summary>
        void CollectStageLights()
        {
            directionalLights.Clear();
            spotLights.Clear();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type == LightType.Directional) directionalLights.Add(light);
                    else if (light.type == LightType.Spot) spotLights.Add(light);
                }
        }

        /// <summary>第一、二阶段只亮平行光；第三阶段平行光关掉，只亮聚光灯。</summary>
        void ApplyStageLighting(bool stage3)
        {
            foreach (var light in directionalLights) if (light != null) light.enabled = !stage3;
            foreach (var light in spotLights) if (light != null) light.enabled = stage3;
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
