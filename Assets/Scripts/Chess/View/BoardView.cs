using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋盘的显示：64 个格子 + 所有棋子的 PieceView。棋盘平放在根物体的 XZ 平面上，
    /// 格子 (x, y) 的中心在本地坐标 (x - 3.5, 0, y - 3.5)。
    /// </summary>
    public sealed class BoardView
    {
        static readonly Color Light = new Color(0.93f, 0.87f, 0.74f);
        static readonly Color Dark = new Color(0.72f, 0.56f, 0.4f);
        static readonly Color Lava = new Color(1f, 0.32f, 0.05f);
        static readonly Color Selected = new Color(1f, 0.9f, 0.3f);
        static readonly Color MoveTarget = new Color(0.55f, 0.85f, 0.45f);
        static readonly Color CaptureTarget = new Color(0.95f, 0.4f, 0.35f);
        static readonly Color PickTarget = new Color(0.35f, 0.8f, 0.95f);
        static readonly Color LastMove = new Color(0.95f, 0.8f, 0.5f);

        readonly Transform root;
        readonly ChessViewFactory factory;
        readonly GameObject[] squares = new GameObject[64];
        readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>();
        readonly Transform pieceRoot;
        Board board;

        public BoardView(Transform parent, ChessViewFactory factory)
        {
            this.factory = factory;
            root = new GameObject("Board").transform;
            root.SetParent(parent, false);

            var frame = factory.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.12f, 0), new Vector3(9f, 0.1f, 9f));
            factory.SetColor(frame, new Color(0.3f, 0.2f, 0.14f));
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                squares[i] = factory.Primitive(PrimitiveType.Cube, root, SquareCenter(c) + new Vector3(0, -0.05f, 0), new Vector3(1f, 0.1f, 1f));
                squares[i].name = c.ToString();
            }
            for (int i = 0; i < 8; i++)
            {
                var file = factory.Label(root, new Vector3(i - 3.5f, 0.01f, -4.25f), 0.035f, ((char)('a' + i)).ToString());
                file.color = new Color(0.95f, 0.9f, 0.8f);
                var rank = factory.Label(root, new Vector3(-4.25f, 0.01f, i - 3.5f), 0.035f, (i + 1).ToString());
                rank.color = new Color(0.95f, 0.9f, 0.8f);
            }
            pieceRoot = new GameObject("Pieces").transform;
            pieceRoot.SetParent(root, false);
        }

        public static Vector3 SquareCenter(Coord c) => new Vector3(c.X - 3.5f, 0f, c.Y - 3.5f);

        /// <summary>屏幕射线落在哪个格子上。</summary>
        public bool TryGetCoord(Ray ray, out Coord coord)
        {
            coord = default;
            var plane = new Plane(root.up, root.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            var local = root.InverseTransformPoint(ray.GetPoint(distance));
            coord = new Coord(Mathf.FloorToInt(local.x + 4f), Mathf.FloorToInt(local.z + 4f));
            return coord.IsValid;
        }

        /// <summary>换一局新棋：清掉旧棋子，按新棋盘重建。</summary>
        public void Bind(Board newBoard)
        {
            foreach (var view in pieces.Values) view.Destroy();
            pieces.Clear();
            board = newBoard;
            SyncAll();
            Highlight(null, null, null, null);
        }

        /// <summary>按棋盘状态把所有棋子摆正（演出结束后调用，保证显示和逻辑一致）。</summary>
        public void SyncAll()
        {
            foreach (var piece in board.AllPieces)
            {
                pieces.TryGetValue(piece.Id, out var view);
                if (!piece.OnBoard)
                {
                    if (view != null)
                    {
                        view.Destroy();
                        pieces.Remove(piece.Id);
                    }
                    continue;
                }
                if (view == null) view = Create(piece);
                view.Root.SetActive(!piece.IsHidden);
                view.Root.transform.localScale = Vector3.one;
                view.SetPosition(SquareCenter(piece.Pos));
                view.Refresh(piece);
            }
        }

        PieceView Create(Piece piece)
        {
            var view = new PieceView(factory, pieceRoot, piece);
            view.SetPosition(SquareCenter(piece.Pos));
            pieces[piece.Id] = view;
            return view;
        }

        public void RefreshPiece(int id)
        {
            if (pieces.TryGetValue(id, out var view)) view.Refresh(board.GetPiece(id));
        }

        /// <summary>
        /// 格子着色。优先级：岩浆 > 点选目标 > 可吃 > 可走 > 选中 > 上一步 > 普通。
        /// </summary>
        public void Highlight(Coord? selected, IEnumerable<Move> moves, IEnumerable<Coord> pickOptions, Move? lastMove)
        {
            var colors = new Color[64];
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                colors[i] = (c.X + c.Y) % 2 == 0 ? Dark : Light;
            }
            if (lastMove.HasValue)
            {
                colors[lastMove.Value.From.Index] = Color.Lerp(colors[lastMove.Value.From.Index], LastMove, 0.6f);
                colors[lastMove.Value.To.Index] = Color.Lerp(colors[lastMove.Value.To.Index], LastMove, 0.6f);
            }
            if (selected.HasValue) colors[selected.Value.Index] = Selected;
            if (moves != null)
                foreach (var m in moves)
                    colors[m.To.Index] = board.PieceAt(m.To) != null ? CaptureTarget : MoveTarget;
            if (pickOptions != null)
                foreach (var c in pickOptions)
                    colors[c.Index] = PickTarget;
            for (int i = 0; i < 64; i++)
            {
                if (board != null && board.IsLava(Coord.FromIndex(i))) colors[i] = Lava;
                factory.SetColor(squares[i], colors[i]);
            }
        }

        // ---------- 演出 ----------

        public IEnumerator AnimateMove(int id, Coord from, Coord to, float duration)
        {
            var piece = board.GetPiece(id);
            if (!pieces.TryGetValue(id, out var view)) view = Create(piece);
            view.Root.SetActive(true); // 从城堡里出来的棋子之前是隐藏的
            var start = SquareCenter(from);
            var end = SquareCenter(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                view.SetPosition(Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f);
                yield return null;
            }
            view.SetPosition(end);
        }

        public IEnumerator AnimateCapture(int id, bool burned, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            pieces.Remove(id);
            if (burned)
            {
                var flame = factory.Primitive(PrimitiveType.Sphere, view.Root.transform, new Vector3(0, 0.4f, 0), Vector3.one * 0.9f);
                factory.SetColor(flame, new Color(1f, 0.5f, 0.05f));
                yield return Wait(duration);
            }
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            view.Destroy();
        }

        public IEnumerator AnimateSpawn(int id, float duration)
        {
            var piece = board.GetPiece(id);
            if (!pieces.TryGetValue(id, out var view)) view = Create(piece);
            view.Refresh(piece);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            view.Root.transform.localScale = Vector3.one;
        }

        public IEnumerator AnimateEnterCastle(int id, Coord castle, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            var start = view.Root.transform.localPosition;
            var end = SquareCenter(castle);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.SetPosition(Vector3.Lerp(start, end, Mathf.SmoothStep(0, 1, t)) + Vector3.up * 0.6f * Mathf.Sin(t * Mathf.PI));
                view.Root.transform.localScale = Vector3.one * (1f - 0.7f * t);
                yield return null;
            }
            view.Root.SetActive(false);
        }

        /// <summary>外观变化时轻轻弹一下，让玩家注意到。</summary>
        public IEnumerator AnimatePop(int id, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            view.Refresh(board.GetPiece(id));
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            view.Root.transform.localScale = Vector3.one;
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) yield return null;
        }
    }
}
