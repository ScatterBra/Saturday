using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 让斜视相机自动框住整张桌面布局（左边规则卡区、棋盘、右边规则书）。
    /// 俯仰角和水平转角固定，只调整相机离桌面的距离和注视点，让布局在屏幕上居中；屏幕比例变了会重新计算。
    /// 挂在棋盘相机上，相机需要是 Chess 根物体的子物体。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DeskCameraFit : MonoBehaviour
    {
        [Tooltip("俯仰角，90 = 正上方往下看")]
        [Range(0f, 90f)] public float pitch = 65f;
        [Tooltip("水平转角，0 = 从白方正后方看过去；正数相机往右绕，负数往左绕")]
        [Range(-90f, 90f)] public float yaw = 0f;
        [Tooltip("缩放，1 = 刚好框住整个布局；大于 1 拉近（边缘会被裁掉），小于 1 拉远（四周露出更多桌面）")]
        [Range(0.5f, 3f)] public float zoom = 1f;
        [Tooltip("布局左右、前后的半宽（本地坐标），默认按 ChessHud.DeskLayout 计算")]
        public Vector2 halfExtent = new Vector2(ChessHud.DeskLayout.HalfExtentX, ChessHud.DeskLayout.HalfExtentZ);
        [Tooltip("屏幕上方留给状态栏的比例")]
        [Range(0f, 0.3f)] public float topMargin = 0.08f;
        [Range(0f, 0.3f)] public float bottomMargin = 0.03f;
        [Range(0f, 0.3f)] public float sideMargin = 0.02f;

        /// <summary>除了平面布局的四个角，还必须框进画面的点（本地坐标），例如第三阶段升起来的城堡顶。改完调用 Fit()。</summary>
        [System.NonSerialized] public List<Vector3> extraPoints = new List<Vector3>();

        Camera cam;
        float fittedAspect;

        void Awake()
        {
            cam = GetComponent<Camera>();
            Fit();
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(cam.aspect, fittedAspect)) Fit();
        }

        // 运行中在 Inspector 里改参数时立刻重新取景
        void OnValidate()
        {
            if (Application.isPlaying && cam != null) Fit();
        }

        [ContextMenu("Fit Now")]
        public void Fit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            fittedAspect = cam.aspect;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.localRotation = rotation;

            // 二分查找最小距离：在这个距离上，移动注视点让布局在屏幕上居中，再看四个角是否都在边距内
            float near = 1f, far = 200f;
            Vector3 focus;
            for (int i = 0; i < 40; i++)
            {
                float distance = (near + far) / 2;
                focus = Center(rotation, distance);
                if (Fits(rotation, distance, focus)) far = distance;
                else near = distance;
            }
            // 刚好框住的距离除以 zoom：拉近或拉远后布局仍然保持居中
            float finalDistance = far / Mathf.Max(0.01f, zoom);
            focus = Center(rotation, finalDistance);
            Place(rotation, finalDistance, focus);
        }

        /// <summary>在 duration 秒内平滑转到新的俯仰角，转的过程中一直保持布局框在屏幕里。</summary>
        public IEnumerator TurnTo(float targetPitch, float duration)
        {
            float start = pitch;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
            {
                pitch = Mathf.Lerp(start, targetPitch, Mathf.SmoothStep(0f, 1f, t));
                Fit();
                yield return null;
            }
            pitch = targetPitch;
            Fit();
        }

        /// <summary>
        /// 找一个注视点，让四个角在屏幕上的包围框处于边距之间的正中。
        /// 沿相机在桌面上的“前后”方向移动修正上下偏差，沿“左右”方向修正左右偏差。
        /// </summary>
        Vector3 Center(Quaternion rotation, float distance)
        {
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            float depthExtent = Extent(forward), widthExtent = Extent(right);
            float wantedMidY = (bottomMargin + (1f - topMargin)) / 2;

            var focus = Vector3.zero;
            for (int i = 0; i < 16; i++)
            {
                Place(rotation, distance, focus);
                Bounds(out float minX, out float maxX, out float minY, out float maxY);
                float spanX = maxX - minX, spanY = maxY - minY;
                if (spanX <= 0.0001f || spanY <= 0.0001f) break;
                // 包围框偏上就把注视点往远处挪，偏右就往右挪
                focus += forward * (((minY + maxY) / 2 - wantedMidY) / spanY * depthExtent);
                focus += right * (((minX + maxX) / 2 - 0.5f) / spanX * widthExtent);
            }
            return focus;
        }

        /// <summary>布局四个角投影到某个水平方向上的总长度。</summary>
        float Extent(Vector3 axis) =>
            2f * (Mathf.Abs(axis.x) * halfExtent.x + Mathf.Abs(axis.z) * halfExtent.y);

        void Bounds(out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            foreach (var corner in Corners())
            {
                var v = Viewport(corner);
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
            }
        }

        bool Fits(Quaternion rotation, float distance, Vector3 focus)
        {
            Place(rotation, distance, focus);
            foreach (var corner in Corners())
            {
                var v = Viewport(corner);
                if (v.z <= 0 || v.x < sideMargin || v.x > 1 - sideMargin || v.y < bottomMargin || v.y > 1 - topMargin)
                    return false;
            }
            return true;
        }

        List<Vector3> Corners()
        {
            var corners = new List<Vector3>
            {
                new Vector3(-halfExtent.x, 0, -halfExtent.y), new Vector3(halfExtent.x, 0, -halfExtent.y),
                new Vector3(-halfExtent.x, 0, halfExtent.y), new Vector3(halfExtent.x, 0, halfExtent.y),
            };
            if (extraPoints != null) corners.AddRange(extraPoints);
            return corners;
        }

        void Place(Quaternion rotation, float distance, Vector3 focus) =>
            transform.localPosition = focus - rotation * Vector3.forward * distance;

        Vector3 Viewport(Vector3 local)
        {
            var parent = transform.parent;
            var world = parent != null ? parent.TransformPoint(local) : local;
            return cam.WorldToViewportPoint(world);
        }
    }
}
