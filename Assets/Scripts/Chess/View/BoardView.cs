using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋盘的显示：64 个格子 + 所有棋子的 PieceView。棋盘平放在根物体的 XZ 平面上，
    /// 格子 (x, y) 的中心在本地坐标 (x - 3.5, 0, y - 3.5)。
    /// 第三阶段的高原用一格一格升起的方块表示，每级高 LevelHeight；城堡格是灰色石块。
    /// </summary>
    public sealed class BoardView
    {
        /// <summary>高原每升一级在画面上抬高多少（世界单位，一格 = 1）。</summary>
        public const float LevelHeight = 0.5f;

        static readonly Color Light = new Color(0.93f, 0.87f, 0.74f);
        static readonly Color Dark = new Color(0.72f, 0.56f, 0.4f);
        static readonly Color Lava = new Color(1f, 0.32f, 0.05f);
        static readonly Color Rock = new Color(0.55f, 0.5f, 0.42f);
        static readonly Color CastleStone = new Color(0.62f, 0.62f, 0.66f);
        static readonly Color Selected = new Color(1f, 0.9f, 0.3f);
        static readonly Color MoveTarget = new Color(0.55f, 0.85f, 0.45f);
        static readonly Color CaptureTarget = new Color(0.95f, 0.4f, 0.35f);
        static readonly Color PickTarget = new Color(0.35f, 0.8f, 0.95f);
        static readonly Color LastMove = new Color(0.95f, 0.8f, 0.5f);
        static readonly Color Danger = new Color(0.9f, 0.1f, 0.1f);

        readonly Transform root;
        readonly ChessViewFactory factory;
        readonly GameObject[] squares = new GameObject[64];
        readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>();
        readonly Transform pieceRoot;
        GameObject castleLabel;
        Board board;

        public BoardView(Transform parent, ChessViewFactory factory)
        {
            this.factory = factory;
            root = new GameObject("Board").transform;
            root.SetParent(parent, false);

            // 桌面（backdrop）：顶面在 y = -0.08，比相机能看到的范围大很多，宽屏也不会露边
            var desk = factory.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.13f, 0), new Vector3(80f, 0.1f, 50f), surface: true);
            desk.name = "Desk";
            factory.SetColor(desk, new Color(0.42f, 0.3f, 0.22f));
            var frame = factory.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.12f, 0), new Vector3(9f, 0.1f, 9f), surface: true);
            factory.SetColor(frame, new Color(0.3f, 0.2f, 0.14f));
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                squares[i] = factory.Primitive(PrimitiveType.Cube, root, SquareCenter(c) + new Vector3(0, -0.05f, 0), new Vector3(1f, 0.1f, 1f), surface: true);
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

        /// <summary>棋子站在这个格子上时的位置（考虑高原高度）。</summary>
        public Vector3 PiecePosition(Coord c) => SquareCenter(c) + Vector3.up * TopOf(c);

        float TopOf(Coord c) => board != null ? board.Height(c) * LevelHeight : 0f;

        /// <summary>屏幕射线落在哪个格子上（升起的格子按它的顶面算）。</summary>
        public bool TryGetCoord(Ray ray, out Coord coord)
        {
            coord = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                var top = root.TransformPoint(SquareCenter(c) + Vector3.up * TopOf(c));
                var plane = new Plane(root.up, top);
                if (!plane.Raycast(ray, out float distance) || distance >= best) continue;
                var local = root.InverseTransformPoint(ray.GetPoint(distance)) - SquareCenter(c);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f) continue;
                best = distance;
                coord = c;
                found = true;
            }
            return found;
        }

        /// <summary>换一局新棋：清掉旧棋子，按新棋盘重建。</summary>
        public void Bind(Board newBoard)
        {
            foreach (var view in pieces.Values) view.Destroy();
            pieces.Clear();
            board = newBoard;
            RebuildTerrain();
            SyncAll();
            Highlight(null, null, null, null);
        }

        /// <summary>按棋盘的高度和城堡重新摆格子（第三阶段风暴之后调用）。</summary>
        public void RebuildTerrain()
        {
            Vector3 castleSum = Vector3.zero;
            int castleCount = 0;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                float top = TopOf(c);
                float thickness = 0.1f + top;
                squares[i].transform.localPosition = SquareCenter(c) + Vector3.up * (top - thickness / 2);
                squares[i].transform.localScale = new Vector3(1f, thickness, 1f);
                if (board != null && board.IsCastle(c))
                {
                    castleSum += SquareCenter(c) + Vector3.up * top;
                    castleCount++;
                }
            }

            if (castleLabel != null) Object.Destroy(castleLabel);
            castleLabel = null;
            if (castleCount > 0)
            {
                var label = factory.Label(root, castleSum / castleCount + Vector3.up * 0.8f, 0.05f, "Princess");
                label.color = new Color(1f, 0.85f, 0.95f);
                label.fontStyle = FontStyle.Bold;
                castleLabel = label.gameObject;
            }
        }

        /// <summary>
        /// 升起来的地形里必须框进画面的点（城堡顶的四角和 Princess 字样），坐标在 Chess 根物体的本地空间，
        /// 给 DeskCameraFit 用。没有城堡时为空。
        /// </summary>
        public List<Vector3> TallPoints()
        {
            var points = new List<Vector3>();
            if (board == null) return points;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (!board.IsCastle(c)) continue;
                var top = SquareCenter(c) + Vector3.up * TopOf(c);
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                    points.Add(ToParent(top + new Vector3(dx * 0.5f, 0, dz * 0.5f)));
            }
            if (castleLabel != null) points.Add(ToParent(castleLabel.transform.localPosition + Vector3.up * 0.3f));
            return points;
        }

        Vector3 ToParent(Vector3 local) =>
            root.parent != null ? root.parent.InverseTransformPoint(root.TransformPoint(local)) : root.TransformPoint(local);

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
                view.Root.SetActive(true);
                view.Root.transform.localScale = Vector3.one;
                view.SetPosition(PiecePosition(piece.Pos));
                view.Refresh(piece);
            }
        }

        PieceView Create(Piece piece)
        {
            var view = new PieceView(factory, pieceRoot, piece);
            view.SetPosition(PiecePosition(piece.Pos));
            pieces[piece.Id] = view;
            return view;
        }

        public void RefreshPiece(int id)
        {
            if (pieces.TryGetValue(id, out var view)) view.Refresh(board.GetPiece(id));
        }

        /// <summary>
        /// 格子着色。优先级：岩浆 > 城堡 > 被将军的王 > 点选目标 > 可吃 > 可走 > 选中 > 上一步 > 普通。
        /// 第三阶段献祭完成前，我方还去不了的对面格子会暗一些；高原是岩石色。
        /// </summary>
        public void Highlight(Coord? selected, IEnumerable<Move> moves, IEnumerable<Coord> pickOptions, Move? lastMove, Coord? danger = null)
        {
            var colors = new Color[64];
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                colors[i] = (c.X + c.Y) % 2 == 0 ? Dark : Light;
                if (board != null && board.Stage3)
                {
                    if (board.Height(c) > 0) colors[i] = Color.Lerp(colors[i], Rock, 0.6f);
                    if (!board.TaskComplete && !board.InPlayerZone(c)) colors[i] = Color.Lerp(colors[i], Color.black, 0.2f);
                }
            }
            if (lastMove.HasValue)
            {
                colors[lastMove.Value.From.Index] = Color.Lerp(colors[lastMove.Value.From.Index], LastMove, 0.6f);
                colors[lastMove.Value.To.Index] = Color.Lerp(colors[lastMove.Value.To.Index], LastMove, 0.6f);
            }
            if (selected.HasValue) colors[selected.Value.Index] = Selected;
            if (moves != null)
                foreach (var m in moves)
                    colors[m.To.Index] = board.PieceAt(m.To) != null || board.IsLava(m.To) ? CaptureTarget : MoveTarget;
            if (pickOptions != null)
                foreach (var c in pickOptions)
                    colors[c.Index] = PickTarget;
            if (danger.HasValue) colors[danger.Value.Index] = Danger;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board != null && board.IsCastle(c)) colors[i] = CastleStone;
                if (board != null && board.IsLava(c))
                {
                    // 岩浆格上能走（献祭 / 地狱马）时用红色描出来，否则就是岩浆色
                    bool target = false;
                    if (moves != null) foreach (var m in moves) target |= m.To == c;
                    colors[i] = target ? Color.Lerp(Lava, CaptureTarget, 0.6f) : Lava;
                }
                factory.SetColor(squares[i], colors[i]);
            }
        }

        // ---------- 演出 ----------

        public IEnumerator AnimateMove(int id, Coord from, Coord to, float duration)
        {
            var piece = board.GetPiece(id);
            if (!pieces.TryGetValue(id, out var view)) view = Create(piece);
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                view.SetPosition(Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f);
                yield return null;
            }
            view.SetPosition(end);
        }

        /// <summary>被击飞：高高地抛出去，落到 to（掉进岩浆的话由随后的 Captured 事件烧掉）。</summary>
        public IEnumerator AnimateKnockback(int id, Coord to, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.SetPosition(Vector3.Lerp(start, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f);
                view.Root.transform.localRotation = Quaternion.Euler(0, 0, 360f * t);
                yield return null;
            }
            view.Root.transform.localRotation = Quaternion.identity;
            view.SetPosition(end);
        }

        /// <summary>被消防栓喷开：贴着棋盘滑过去，先快后慢，最后晃一下。</summary>
        public IEnumerator AnimatePush(int id, Coord to, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            var start = view.Root.transform.localPosition;
            var end = PiecePosition(to);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float k = 1f - (1f - t) * (1f - t);
                view.SetPosition(Vector3.Lerp(start, end, k));
                view.Root.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI * 3f) * 12f * (1f - t));
                yield return null;
            }
            view.Root.transform.localRotation = Quaternion.identity;
            view.SetPosition(end);
        }

        /// <summary>献祭：走进岩浆，慢慢沉下去。</summary>
        public IEnumerator AnimateSacrifice(int id, Coord at, float duration)
        {
            if (!pieces.TryGetValue(id, out var view)) yield break;
            pieces.Remove(id);
            yield return MoveView(view, PiecePosition(at), duration);
            var flame = factory.Primitive(PrimitiveType.Sphere, view.Root.transform, new Vector3(0, 0.3f, 0), Vector3.one * 0.8f);
            factory.SetColor(flame, new Color(1f, 0.3f, 0.05f));
            var start = view.Root.transform.localPosition;
            for (float t = 0; t < 1f; t += Time.deltaTime / (duration * 2))
            {
                view.SetPosition(start + Vector3.down * 0.6f * t);
                view.Root.transform.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            view.Destroy();
        }

        IEnumerator MoveView(PieceView view, Vector3 end, float duration)
        {
            var start = view.Root.transform.localPosition;
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
            view.SetPosition(PiecePosition(piece.Pos));
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                view.Root.transform.localScale = Vector3.one * Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            view.Root.transform.localScale = Vector3.one;
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
