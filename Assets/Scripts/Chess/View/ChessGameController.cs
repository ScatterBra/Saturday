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
        [Tooltip("第三阶段每隔几回合弹一次卡牌（两张挑一张拿到手里）")]
        public int stage3SkillEvery = 5;
        [Tooltip("第三阶段开始后的第几回合王后放技能（鸟群，前一回合先预告）；列表放完后每隔 Friend Skill Every 回合再放一次")]
        public int[] friendSkillTurns = { 2 };
        [Tooltip("王后放完列表里的技能后，每隔几回合再放一次（0 = 不再放）")]
        public int friendSkillEvery = 4;
        [Tooltip("象的光束、火龙用完后要等几回合")]
        public int beamCooldown = 3;
        public int dragonCooldown = 3;
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
        [Tooltip("第三阶段的开场过场（Chess 物体上的 Stage3Cutscene），留空时自动在本物体上找；找不到就用沙尘风暴")]
        public Stage3Cutscene stage3Cutscene;
        [Tooltip("标题字体（NEW、Rule Book、卡片标题、状态栏、说话人、结束文字、按钮、棋子上的小字），留空用 Unity 默认字体加粗")]
        public Font titleFont;
        [Tooltip("正文字体（规则描述、规则书、提示、对白），留空用 Unity 默认字体")]
        public Font bodyFont;
        [Tooltip("打开后 3D 模型（例如树）投影子，桌面和棋盘格子改用受光照的材质来接影子（颜色会随灯光明暗变化）")]
        public bool shadows = true;
        [Tooltip("打开阴影时桌面和棋盘格子用的受光照材质（URP Lit），留空则运行时创建")]
        public Material surfaceMaterial;
        [Tooltip("第一、二阶段的天空盒（例如用 HDRI 做的 Stage12Skybox），也给 3D 模型提供环境光。进第三阶段时跟着灯光一起慢慢变暗，换回相机原来的纯色背景。留空就一直是纯色背景")]
        public Material stage12Skybox;
        [Tooltip("第三阶段（风暴里）房间暗到原来亮度的多少（0 = 全黑，1 = 不变）")]
        [Range(0f, 1f)] public float stage3RoomBrightness = 0.2f;
        [Tooltip("第三阶段房间的色调（天空盒的 Tint，默认灰 = 0.5；偏蓝显得冷）")]
        public Color stage3RoomTint = new Color(0.38f, 0.42f, 0.55f, 0.5f);
        public float aiThinkTime = 0.6f;
        public float moveDuration = 0.25f;
        public float effectDuration = 0.35f;
        [Tooltip("临时变身的棋子变回原样（例如喷完火的龙变回马）时弹一下的时长，比普通换外观慢")]
        public float revertDuration = 0.9f;
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
        [Tooltip("调试用：对白不用点击，停一会儿自动继续")]
        public bool autoContinueDialogue;

        ChessGame game;
        ChessAI ai;
        BoardView board;
        ChessHud hud;
        DeskCameraFit cameraFit;
        float startPitch;
        float startFieldOfView;
        float startZoom;
        bool cameraTurning;
        /// <summary>规则书：鼠标停上去描边，点一下举到镜头前，点外面放回去。</summary>
        RuleBookFocus ruleBookFocus;
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
        /// <summary>风暴事件选了播过场：等 BoardRebuilt 事件时再播（那时候棋盘已经可以演第三阶段了）。</summary>
        bool cutscenePending;
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
            if (stage3Cutscene == null) stage3Cutscene = GetComponent<Stage3Cutscene>();
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
            SetUpSky();
            var factory = new ChessViewFactory(placeholderMaterial, boardCamera, pieceArt, surfaceMaterial, shadows, titleFont, bodyFont,
                cameraFit != null ? cameraFit.ViewReference : null);
            if (desk == null) desk = GetComponentInChildren<ChessDesk>();
            board = new BoardView(transform, factory, desk);
            hud = ChessHud.Create(transform, boardCamera, pieceArt, desk, titleFont, bodyFont);
            hud.RuleCardClicked += ChooseRule;
            hud.SkipClicked += Skip;
            hud.LostPieceClicked += id => ResolvePiece(id);
            hud.SkillClicked += UseSkill;
            hud.PieceSkillClicked += OnPieceSkillClicked;
            hud.PlayAgainClicked += NewGame;
            hud.BackClicked += () => MinigameSession.Finish(game.Winner == game.PlayerSide ? "won" : game.Winner == null ? "draw" : "lost");
            if (desk != null) ruleBookFocus = RuleBookFocus.Attach(desk.ruleBook, boardCamera, desk.ruleBookOutline);
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
            if (stage3Cutscene != null)
            {
                stage3Cutscene.Abort();
                stage3Cutscene.StopMusic();
            }
            cutscenePending = false;
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
                FriendSkillEvery = friendSkillEvery,
                BeamCooldown = beamCooldown,
                DragonCooldown = dragonCooldown,
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
            castingPiece = null;
            selectAfterEvents = null;
            board.SetWarnings(null);
            board.Bind(game.Board);
            // 新的一局总是从第一、二阶段的灯光和天空开始。调试跳关时逻辑开局就进了第三阶段，
            // 但后面一定跟着风暴事件，由过场（或沙尘风暴那条路）再换成第三阶段的灯光
            ApplyStageLighting(false);
            hud.ResetForNewGame();
            if (ruleBookFocus != null) ruleBookFocus.PutBack();
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
            // 规则书举着（或鼠标在规则书上点了）的时候，棋盘不响应
            if (UpdateRuleBook()) return;
            UpdateTooltip();
            UpdatePieceSkillButton();
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
            UpdateSkillPreview(mouse.position.ReadValue());
            // 右键原地单击 = 取消选中；点选技能目标时 = 跳过（拖动转视角在 UpdateOrbit 里）
            if (mouse.rightButton.wasReleasedThisFrame && !rightDragging)
            {
                if (game.Phase == GamePhase.ChoosingTarget && game.PendingTarget.AllowSkip) Skip();
                else Deselect();
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && game.Phase == GamePhase.ChoosingTarget && game.PendingTarget.AllowSkip) Skip();
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (board.TryGetPointedCoord(boardCamera.ScreenPointToRay(mouse.position.ReadValue()), out var coord)) ClickSquare(coord);
        }

        /// <summary>
        /// 规则书：鼠标停上去描边，左键点一下举到镜头前；举着时点外面或按 Esc 放回去。
        /// 演出、转相机的时候不能拿起放下。返回 true = 规则书举着或这一下点在规则书上，这一帧别的输入都不处理。
        /// </summary>
        bool UpdateRuleBook()
        {
            var mouse = Mouse.current;
            if (ruleBookFocus == null || mouse == null || boardCamera == null) return false;
            var keyboard = Keyboard.current;
            bool canInteract = !busy && !cameraTurning && game.Phase != GamePhase.GameOver;
            bool consumed = ruleBookFocus.Tick(boardCamera.ScreenPointToRay(mouse.position.ReadValue()),
                mouse.leftButton.wasPressedThisFrame, keyboard != null && keyboard.escapeKey.wasPressedThisFrame, canInteract);
            if (consumed)
            {
                hud.HideTooltip();
                hud.HidePieceSkill();
                board.HideGhost();
            }
            return consumed;
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
                PieceTooltip.HasTooltip(game.Board.GetPiece(hit), game.Board))
                id = hit;
            if (id != hoverPieceId)
            {
                hoverPieceId = id;
                hoverSince = Time.unscaledTime;
            }
            var info = id >= 0 && Time.unscaledTime - hoverSince >= tooltipDelay
                ? PieceTooltip.Describe(game.Board.GetPiece(id), game.PlayerSide, game.InStage3, game.Board)
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
            // 规则书举在镜头前时不能转视角、不能缩放
            bool canOrbit = orbitUnlocked && !cameraTurning && (ruleBookFocus == null || !ruleBookFocus.Active);
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
                var request = game.PendingTarget;
                if (request.IsPieceRequest) return;
                if (request.Options.Contains(coord)) ResolveTarget(coord);
                else if (request.Origin.HasValue && request.AllowSkip && game.InStage3)
                {
                    // 像下棋一样：点别处就取消（第三阶段的技能取消了不算用过）；点的是自己的棋子就顺手选中它
                    var other = game.Board.PieceAt(coord);
                    selectAfterEvents = other != null && other.Side == game.PlayerSide ? other : null;
                    ResolveTarget(null);
                }
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

        /// <summary>第三阶段技能栏：放常驻技能（象的光束、火龙、雷暴）。</summary>
        public void UseSkill(Stage3Skill skill)
        {
            if (busy || !game.CanUseSkill(skill)) return;
            Deselect();
            game.UseSkill(skill);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        /// <summary>第三阶段：放这枚棋子（象、马）身上的技能，不用再选棋子。</summary>
        public void UsePieceSkill(Piece piece)
        {
            var skill = ChessGame.PieceSkill(piece);
            if (busy || skill == null || !game.CanUseSkill(skill.Value, piece)) return;
            Deselect();
            castingPiece = piece;
            game.UseSkill(skill.Value, piece);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        /// <summary>点了棋子头顶的技能图标：没在放就开始放；正在选目标就取消，回到这枚棋子的走法。</summary>
        void OnPieceSkillClicked()
        {
            if (busy) return;
            if (castingPiece != null && game.Phase == GamePhase.ChoosingTarget)
            {
                if (!game.PendingTarget.AllowSkip) return;
                selectAfterEvents = castingPiece;
                ResolveTarget(null);
                return;
            }
            if (selected != null) UsePieceSkill(selected);
        }

        /// <summary>
        /// 第三阶段选中象或马时，在它头顶弹出技能图标（冷却中、够不着时变灰并写原因）；
        /// 放技能选目标时图标亮着，写“Click to cancel”。演出中、相机在转时收起来。
        /// </summary>
        void UpdatePieceSkillButton()
        {
            var piece = castingPiece != null && game.Phase == GamePhase.ChoosingTarget ? castingPiece
                : game.Phase == GamePhase.PlayerTurn ? selected : null;
            var skill = piece != null && game.InStage3 && piece.Side == game.PlayerSide && piece.OnBoard ? ChessGame.PieceSkill(piece) : null;
            if (busy || cameraTurning || skill == null || !board.TryGetPieceTop(piece.Id, out var top))
            {
                hud.HidePieceSkill();
                return;
            }
            var screen = boardCamera.WorldToScreenPoint(top);
            if (screen.z <= 0f)
            {
                hud.HidePieceSkill();
                return;
            }
            bool active = piece == castingPiece;
            int cooldown = game.Cooldown(skill.Value);
            bool usable = active || game.CanUseSkill(skill.Value, piece);
            string detail = active ? "Click to cancel"
                : piece.IsFrozen ? "Frozen"
                : cooldown > 0 ? (cooldown == 1 ? "Ready next turn" : $"Ready in {cooldown} turns")
                : usable ? "Takes your turn"
                : "Nothing in reach";
            var icon = desk == null ? null : skill == Stage3Skill.Beam ? desk.beamIcon : desk.dragonIcon;
            hud.ShowPieceSkill(screen, icon, skill == Stage3Skill.Beam ? "Beam" : "Dragon", detail, usable, active);
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
                var options = result.CastleTaken ? new List<HydrantSpray.Result>() : FireHydrantRule.EffectCandidates(next, next.GetPiece(move.PieceId));
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

        Coord? skillPreviewSquare;
        /// <summary>取消像走法一样显示的技能时点到的自己的棋子，事件播完后选中它。</summary>
        Piece selectAfterEvents;
        /// <summary>正在放头顶技能的棋子（选目标时它的图标亮着）；不在选目标了就清掉。</summary>
        Piece castingPiece;

        /// <summary>点选格子时的高亮：有 Origin 的（象的光束）像下棋一样显示，其余照普通点选显示。</summary>
        void HighlightRequest(TargetRequest request, IEnumerable<Coord> affected = null)
        {
            if (request.Origin.HasValue)
                board.Highlight(request.Origin, null, null, lastMove, null, null, null, request.Options);
            else
                board.Highlight(null, null, request.Options, lastMove, null, null, affected);
        }

        /// <summary>点选技能目标时（光束的方向、雷暴的范围、自爆的棋子）：鼠标停在哪个可选格子上，就标出会打到哪些格子。</summary>
        void UpdateSkillPreview(Vector2 mousePosition)
        {
            var request = game.Phase == GamePhase.ChoosingTarget ? game.PendingTarget : null;
            // 像走法一样显示的（象的光束）不随鼠标变，和下棋时一样
            if (request == null || request.IsPieceRequest || request.Affected == null || request.Origin.HasValue)
            {
                skillPreviewSquare = null;
                return;
            }
            Coord? hovered = null;
            if (board.TryGetCoord(boardCamera.ScreenPointToRay(mousePosition), out var c) && request.Options.Contains(c)) hovered = c;
            if (hovered == skillPreviewSquare) return;
            skillPreviewSquare = hovered;
            HighlightRequest(request, hovered.HasValue ? request.Affected(hovered.Value) : null);
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
            if (autoContinueDialogue)
            {
                yield return new WaitForSeconds(dialogueDuration);
                yield break;
            }
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
            RefreshSkillBar();
            board.HideGhost();
            board.Highlight(null, null, null, lastMove);
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case GameEventType.Moved:
                        yield return board.AnimateMove(e.PieceId, e.From, e.To, moveDuration);
                        // Bish-bird 落子不是"嗒"一声，是叫一声（没配音效就还是普通落子声）
                        bool bird = game.Board.GetPiece(e.PieceId)?.Skin == "bishbird";
                        PlaySound(s => bird && s.bishBird.clips.Length > 0 ? s.bishBird : s.pieceFall);
                        break;
                    case GameEventType.Pushed:
                        // 推子的是消防栓就是喷水，是青蛙就是舌头
                        var pusher = e.SourcePieceId >= 0 ? game.Board.GetPiece(e.SourcePieceId) : null;
                        if (pusher?.Skin == FireHydrantRule.Hydrant)
                        {
                            PlaySound(s => s.hydrant);
                            board.PlayHydrantSpray(pusher.Pos, e.From);
                        }
                        else if (pusher?.Skin == FireHydrantRule.Frog) PlaySound(s => s.frog);
                        yield return board.AnimatePush(e.PieceId, e.To, effectDuration * 1.5f);
                        break;
                    case GameEventType.KnockedBack:
                        // 爆炸时一圈棋子同时飞出去：每个只等一小会儿，不用一个一个慢慢飞
                        StartCoroutine(board.AnimateKnockback(e.PieceId, e.To, effectDuration * 1.6f));
                        yield return new WaitForSeconds(0.04f);
                        break;
                    case GameEventType.Exploded:
                        PlaySound(s => s.explosion.clips.Length > 0 ? s.explosion : s.burn);
                        board.PlayExplosion(e.To);
                        yield return new WaitForSeconds(0.15f);
                        break;
                    case GameEventType.Beam:
                        PlaySound(s => s.beam);
                        yield return board.AnimateBeam(e.From, e.To, effectDuration * 1.5f);
                        break;
                    case GameEventType.Thunder:
                        PlaySound(s => s.thunder.clips.Length > 0 ? s.thunder : s.burn);
                        yield return board.AnimateThunder(e.Squares, effectDuration * 2.5f);
                        break;
                    case GameEventType.Soaked:
                        PlaySound(s => s.rain.clips.Length > 0 ? s.rain : s.hydrant);
                        board.SetCloudRain(true);
                        board.PlayRain();
                        board.SyncAll();
                        yield return new WaitForSeconds(effectDuration * 2);
                        break;
                    case GameEventType.GateOpened:
                        PlaySound(s => s.gate);
                        board.RefreshCastleLabel();
                        board.SetWarnings(null);
                        board.Highlight(null, null, null, lastMove);
                        hud.ShowCaption(null, "The gate is open!");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.SkillTelegraphed:
                        board.SetWarnings(e.Squares);
                        board.Highlight(null, null, null, lastMove);
                        hud.ShowCaption(Speakers.Friend, $"*the queen prepares {e.Rule.Title}*");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.Notice:
                        hud.ShowCaption(null, e.Text);
                        yield return new WaitForSeconds(dialogueDuration);
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
                        else if (e.Reverted)
                        {
                            // 变回原样（喷完火的龙变回马）：和树一样冒粉尘、播树的音效
                            PlaySound(s => s.tree);
                            board.PlayRevertEffect(e.PieceId);
                        }
                        else if (!e.Frozen && e.Text != null) PlaySound(s => s.pieceChange);
                        yield return board.AnimatePop(e.PieceId, e.Reverted ? revertDuration : effectDuration);
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
                        board.AddLavaEffects(e.Squares);
                        board.Highlight(null, null, null, lastMove);
                        yield return new WaitForSeconds(effectDuration);
                        break;
                    case GameEventType.Storm:
                        // 有开场过场就交给过场演（在 BoardRebuilt 里播）
                        if (stage3Cutscene != null && stage3Cutscene.isActiveAndEnabled && stage3Cutscene.play && boardCamera != null)
                        {
                            cutscenePending = true;
                            break;
                        }
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
                        if (cutscenePending)
                        {
                            cutscenePending = false;
                            yield return PlayStage3Cutscene();
                            break;
                        }
                        board.FinishStage3Reveal();
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
                        board.SetWarnings(null);
                        board.Highlight(null, null, null, lastMove);
                        hud.ShowCaption(Speakers.Friend, game.InStage3 ? $"*the queen casts {e.Rule.Title}*" : $"*casts {e.Rule.Title}*");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.TurnPassed:
                        hud.ShowCaption(null, e.Side == game.PlayerSide ? "You can't move. Turn skipped." : "Your friend can't move. Turn skipped.");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.RulesOffered:
                        yield return ReturnToFront();
                        hud.SetSkipLabel("Skip");
                        hud.ShowOffers(e.Rules, e.OfferKind);
                        // 从上到下一张一张飞进来，每张一声
                        yield return hud.FlyInCards(cardFlyDuration, () => PlaySound(s => s.paperPopup));
                        // 第三阶段的卡不给跳过，挑一张用
                        hud.ShowSkip(e.OfferKind != OfferKind.Stage3);
                        break;
                    case GameEventType.RuleActivated:
                        hud.HideOffers();
                        hud.ShowSkip(false);
                        hud.AddRule(e.Rule);
                        break;
                    case GameEventType.TargetRequested:
                        hud.SetSkipLabel("Skip");
                        if (e.Request.IsPieceRequest) yield return ReturnToFront(); // 要在规则书的阵亡区里点
                        if (!e.Request.IsPieceRequest) HighlightRequest(e.Request);
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
            board.SetWarnings(game.FriendTelegraph);
            board.RefreshCastleLabel();
            // 雷雨云只在有棋子潮湿时下雨
            board.SetCloudRain(game.InStage3 && game.Board.AllPieces.Any(p => p.OnBoard && p.IsWet));
            skillPreviewSquare = null;
            if (game.Phase == GamePhase.ChoosingTarget && !pickingPiece)
                HighlightRequest(game.PendingTarget);
            else
                board.Highlight(null, null, null, lastMove, DangerSquare());
            UpdateStatus();
            busy = false;
            RefreshSkillBar();
            if (game.Phase != GamePhase.ChoosingTarget) castingPiece = null;
            var toSelect = selectAfterEvents;
            selectAfterEvents = null;
            if (toSelect != null && toSelect.OnBoard && toSelect.Side == game.PlayerSide && game.Phase == GamePhase.PlayerTurn) Select(toSelect);
        }

        /// <summary>第三阶段左下角的雷暴按钮和积累条（吃掉敌人就涨，满了点按钮放）；满了云里闪电也变多。不是第三阶段就收起来。</summary>
        void RefreshSkillBar()
        {
            bool show = game.InStage3 && game.Phase != GamePhase.GameOver;
            if (!show)
            {
                hud.SetSkillBar(false, default, 0, 0);
                board.SetCloudCharged(false);
                return;
            }
            int charge = game.ThunderCharge, max = game.ThunderMax;
            bool thunderReady = game.CanUseSkill(Stage3Skill.Thunder) && !busy;
            var thunder = new ChessHud.SkillSlot
            {
                Title = "Thunderstorm",
                Detail = charge < max ? $"Take enemies to charge: {charge}/{max}" : thunderReady ? "READY! Click to strike" : "Charged",
                Interactable = thunderReady,
                Glow = thunderReady,
            };
            hud.SetSkillBar(true, thunder, charge, max);
            board.SetCloudCharged(charge >= max);
        }

        void UpdateStatus()
        {
            int turn = game.Board.FullMove;
            var b = game.Board;
            switch (game.Phase)
            {
                case GamePhase.PlayerTurn:
                    if (game.InStage3)
                        hud.SetStatus(game.GateOpen
                            ? $"Turn {turn} - the gate is open! Walk any piece into the castle"
                            : $"Turn {turn} - {b.GuardCount} guard{(b.GuardCount == 1 ? "" : "s")} left");
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
                        : game.CurrentOfferKind == OfferKind.Stage3
                            ? $"Turn {turn} - pick a card"
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
                if (game.InStage3)
                    hint = "Storm the castle! A new card shows up every few turns.";
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
        void ApplyStageLighting(bool stage3) => BlendStageLighting(stage3 ? 1f : 0f);

        // ---------- 天空 ----------

        /// <summary>运行时用的天空盒副本（改曝光、色调不会改到材质文件），以及原来的天空盒（第三阶段的环境光用它，退出时还原）。</summary>
        Material skyInstance;
        float skyExposure = 1f;
        Color skyTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Material originalSkybox;
        /// <summary>棋盘相机自己的天空盒：一直显示房间（第三阶段暗下来），和管环境光的全局天空盒分开。</summary>
        Skybox cameraSky;
        bool skyShown;

        void SetUpSky()
        {
            originalSkybox = RenderSettings.skybox;
            if (stage12Skybox == null || boardCamera == null) return;
            skyInstance = new Material(stage12Skybox) { name = stage12Skybox.name + " (runtime)", hideFlags = HideFlags.DontSave };
            if (skyInstance.HasProperty("_Exposure")) skyExposure = skyInstance.GetFloat("_Exposure");
            if (skyInstance.HasProperty("_Tint")) skyTint = skyInstance.GetColor("_Tint");
            if (!boardCamera.TryGetComponent(out cameraSky)) cameraSky = boardCamera.gameObject.AddComponent<Skybox>();
            cameraSky.material = skyInstance;
            boardCamera.clearFlags = CameraClearFlags.Skybox;
            // 地面投影的天空盒（Saturday/Skybox Ground Projected）：HDRI 的地板铺在桌面下方，以场景里桌面的位置为准
            if (skyInstance.HasProperty("_DeskTop") && desk != null)
            {
                var top = desk.transform.Find("Background");
                var p = top != null ? top.position : desk.transform.position;
                skyInstance.SetVector("_DeskTop", new Vector4(p.x, p.y, p.z, 0f));
            }
        }

        /// <summary>
        /// 天空跟着阶段灯光走：房间一直在（相机自己的天空盒），t = 0（第一、二阶段）正常亮度；
        /// 往第三阶段过渡时慢慢暗到 stage3RoomBrightness、色调变成 stage3RoomTint，像风暴来了屋里没开灯。
        /// 环境光：第一、二阶段用房间算；t = 1（第三阶段）换回原来的天空盒，第三阶段的灯光不受房间影响。
        /// </summary>
        void ApplySky(float t)
        {
            if (skyInstance == null || boardCamera == null) return;
            if (skyInstance.HasProperty("_Exposure")) skyInstance.SetFloat("_Exposure", skyExposure * Mathf.Lerp(1f, stage3RoomBrightness, t));
            if (skyInstance.HasProperty("_Tint")) skyInstance.SetColor("_Tint", Color.Lerp(skyTint, stage3RoomTint, t));
            bool stage12 = t < 1f;
            if (stage12 == skyShown && RenderSettings.skybox == (stage12 ? skyInstance : originalSkybox)) return;
            skyShown = stage12;
            RenderSettings.skybox = stage12 ? skyInstance : originalSkybox;
            // 环境光（3D 模型的底色）按新的天空重新算一次
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// 天空盒材质上的房间参数（旋转、地板高度、虚化、桌下阴影……）每帧同步到运行时副本，
        /// Play 模式里直接改 Stage12Skybox 材质就能看到效果（曝光、色调、桌面位置由代码管，不同步）。
        /// </summary>
        static readonly string[] SkyLiveFloats = { "_Rotation", "_TableHeight", "_CaptureHeight", "_Radius", "_BlurMax", "_BlurNear", "_BlurFar", "_ShadowSoftness", "_ShadowStrength" };

        void LateUpdate()
        {
            if (skyInstance == null || stage12Skybox == null) return;
            foreach (var name in SkyLiveFloats)
                if (stage12Skybox.HasProperty(name)) skyInstance.SetFloat(name, stage12Skybox.GetFloat(name));
            if (stage12Skybox.HasProperty("_ShadowSize")) skyInstance.SetVector("_ShadowSize", stage12Skybox.GetVector("_ShadowSize"));
        }

        void OnDestroy()
        {
            // 小游戏场景卸载时把全局的天空盒还回去（它是整个游戏共用的设置）
            if (skyInstance != null)
            {
                if (RenderSettings.skybox == skyInstance) RenderSettings.skybox = originalSkybox;
                Destroy(skyInstance);
            }
        }

        /// <summary>灯光在两个阶段之间过渡（开场过场里天慢慢变暗）：0 = 只亮平行光，1 = 只亮聚光灯。</summary>
        void BlendStageLighting(float t)
        {
            t = Mathf.Clamp01(t);
            ApplySky(t);
            foreach (var light in directionalLights)
            {
                if (light == null) continue;
                float full = BaseIntensity(light);
                light.intensity = full * (1f - t);
                light.enabled = t < 1f;
            }
            foreach (var light in spotLights)
            {
                if (light == null) continue;
                float full = BaseIntensity(light);
                light.intensity = full * t;
                light.enabled = t > 0f;
            }
        }

        /// <summary>灯在场景里设的亮度（第一次碰到时记下来，之后按它的比例调）。</summary>
        float BaseIntensity(Light light)
        {
            if (!baseIntensities.TryGetValue(light, out float value))
            {
                value = light.intensity;
                baseIntensities[light] = value;
            }
            return value;
        }

        readonly Dictionary<Light, float> baseIntensities = new Dictionary<Light, float>();

        /// <summary>
        /// 第三阶段的开场过场（Stage3Cutscene）：演完或跳过后棋盘摆成第三阶段的样子，镜头回到第三阶段的视角。
        /// 和沙尘风暴那条路做的事一样，只是换成演出来。
        /// </summary>
        IEnumerator PlayStage3Cutscene()
        {
            cameraTurning = true;
            hud.HideCaption();
            hud.SetStage3(true);
            if (cameraFit != null) cameraFit.SetOrbit(0f, 0f, 1f);
            yield return stage3Cutscene.Play(new Stage3Cutscene.Context
            {
                Board = board,
                Camera = boardCamera,
                CameraFit = cameraFit,
                GamePitch = stage3CameraPitch > 0f ? stage3CameraPitch : cameraFit != null ? cameraFit.pitch : 65f,
                GameFieldOfView = stage3FieldOfView,
                GameZoom = stage3Zoom,
                BlendLighting = BlendStageLighting,
                FinishBoard = () =>
                {
                    board.FinishStage3Reveal();
                    board.SyncAll();
                    ApplyStageLighting(true);
                    board.Highlight(null, null, null, null);
                },
                HudRoot = transform,
            });
            FaceCamera.FollowCameraPitch = true;
            cameraTurning = false;
            orbitUnlocked = true;
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
