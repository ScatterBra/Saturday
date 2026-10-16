using System.Collections;
using System.Collections.Generic;
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
        [Tooltip("在这些回合开始时抽规则卡")]
        public int[] stageTurns = { 4, 10, 16 };
        [Range(0f, 1f)] public float aiBlunderChance = 0.2f;

        [Header("Presentation")]
        public Camera boardCamera;
        [Tooltip("占位用的不受光照材质（URP Unlit），留空则运行时创建")]
        public Material placeholderMaterial;
        [Tooltip("棋子贴图配置，留空则用占位图形")]
        public PieceArt pieceArt;
        public float aiThinkTime = 0.6f;
        public float moveDuration = 0.25f;
        public float effectDuration = 0.35f;
        public float dialogueDuration = 1.4f;

        ChessGame game;
        ChessAI ai;
        BoardView board;
        ChessHud hud;
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
            var factory = new ChessViewFactory(placeholderMaterial, boardCamera, pieceArt);
            board = new BoardView(transform, factory);
            hud = ChessHud.Create(transform, boardCamera);
            hud.RuleCardClicked += ChooseRule;
            hud.SkipClicked += () => ResolveTarget(null);
            hud.PlayAgainClicked += NewGame;
            hud.BackClicked += () => MinigameSession.Finish(game.Winner == game.PlayerSide ? "won" : game.Winner == null ? "draw" : "lost");
            NewGame();
        }

        public void NewGame()
        {
            StopAllCoroutines();
            busy = false;
            var settings = new GameSettings { StageTurns = stageTurns };
            if (seed != 0) settings.Seed = seed;
            game = new ChessGame(settings);
            ai = new ChessAI(new System.Random(settings.Seed + 1)) { BlunderChance = aiBlunderChance };
            selected = null;
            selectedMoves.Clear();
            lastMove = null;
            board.Bind(game.Board);
            hud.ResetForNewGame();
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
                foreach (var option in game.PendingTarget.Options)
                    if (option == coord) ResolveTarget(coord);
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
                // 再点一次城堡：切换成操作藏在里面的棋子
                if (coord == selected.Pos && selected.HasGuest)
                {
                    Select(game.Board.GetPiece(selected.GuestId));
                    return;
                }
                if (coord == selected.Pos && selected.IsHidden)
                {
                    Select(game.Board.GetPiece(selected.HostId));
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
            game.ChooseRule(index);
            StartCoroutine(PlayEvents(game.DrainEvents()));
        }

        public void ResolveTarget(Coord? coord)
        {
            if (busy || game.Phase != GamePhase.ChoosingTarget) return;
            hud.ShowSkip(false);
            game.ResolveTarget(coord);
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
            board.Highlight(piece.Pos, selectedMoves, null, lastMove);
            if (piece.IsHidden)
                hud.SetStatus($"Moving the {Name(piece)} hiding in the castle. Click the castle again to switch back.");
            else if (piece.HasGuest)
                hud.SetStatus("Click the castle again to move the piece hiding inside.");
            else if (piece.IsFrozen)
                hud.SetStatus($"That {Name(piece)} is frozen.");
            else
                UpdateStatus();
        }

        void Deselect()
        {
            selected = null;
            selectedMoves.Clear();
            if (game.Phase != GamePhase.ChoosingTarget) board.Highlight(null, null, null, lastMove);
            UpdateStatus();
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
                    case GameEventType.Captured:
                        yield return board.AnimateCapture(e.PieceId, e.Text == "burned", effectDuration);
                        break;
                    case GameEventType.Promoted:
                    case GameEventType.AppearanceChanged:
                        yield return board.AnimatePop(e.PieceId, effectDuration);
                        break;
                    case GameEventType.Revived:
                        yield return board.AnimateSpawn(e.PieceId, effectDuration * 2);
                        break;
                    case GameEventType.EnteredCastle:
                        yield return board.AnimateEnterCastle(e.PieceId, e.To, effectDuration * 1.5f);
                        board.RefreshPiece(e.SourcePieceId);
                        break;
                    case GameEventType.LavaAdded:
                        board.Highlight(null, null, null, lastMove);
                        yield return new WaitForSeconds(effectDuration);
                        break;
                    case GameEventType.TurnPassed:
                        hud.ShowCaption(null, e.Side == game.PlayerSide ? "You can't move. Turn skipped." : "Your friend can't move. Turn skipped.");
                        yield return new WaitForSeconds(dialogueDuration);
                        break;
                    case GameEventType.RulesOffered:
                        hud.ShowOffers(e.Rules);
                        break;
                    case GameEventType.RuleActivated:
                        hud.HideOffers();
                        hud.AddRule(e.Rule);
                        break;
                    case GameEventType.TargetRequested:
                        board.Highlight(null, null, e.Request.Options, lastMove);
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
            if (game.Phase == GamePhase.ChoosingTarget)
                board.Highlight(null, null, game.PendingTarget.Options, lastMove);
            else
                board.Highlight(null, null, null, lastMove);
            UpdateStatus();
            busy = false;
        }

        void UpdateStatus()
        {
            int turn = game.Board.FullMove;
            switch (game.Phase)
            {
                case GamePhase.PlayerTurn:
                    hud.SetStatus($"Turn {turn} - your move");
                    break;
                case GamePhase.OpponentTurn:
                    hud.SetStatus($"Turn {turn} - your friend is thinking...");
                    break;
                case GamePhase.ChoosingRule:
                    hud.SetStatus($"Turn {turn} - pick a new house rule");
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
                hud.SetNewHint(game.NextStageTurn > 0
                    ? $"A new rule appears on turn {game.NextStageTurn}."
                    : "No more new rules... for now.");
            }
        }

        static string Name(Piece piece) => piece.Type.ToString().ToLowerInvariant();

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
        }
    }
}
