using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 让斜视相机自动框住整张桌面布局（左边规则卡区、棋盘、右边规则书）。
    /// 俯仰角和水平转角固定，只调整相机离桌面的距离和注视点，让布局在屏幕上居中；屏幕比例变了会重新计算。
    /// 挂在棋盘相机上，相机需要是 Chess 根物体的子物体。
    ///
    /// 玩家旋转（Orbit）：在上面算好的“正面视角”之上，绕棋盘中心再转 orbitYaw（水平）和 orbitPitch（俯仰），orbitZoom 拉近拉远。
    /// 旋转为 0 时和正面视角完全一样。棋子朝向跟的是 ViewReference（不含玩家的旋转），所以玩家转视角时棋子保持不动。
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
        [Tooltip("视野角（度）。越小越像长焦，纵深被压扁、相机离得越远；越大近大远小越明显，相机会自动靠近")]
        [Range(10f, 100f)] public float fieldOfView = 30f;
        [Tooltip("布局左右、前后的半宽（本地坐标），默认按 ChessHud.DeskLayout 计算")]
        public Vector2 halfExtent = new Vector2(ChessHud.DeskLayout.HalfExtentX, ChessHud.DeskLayout.HalfExtentZ);
        [Tooltip("屏幕上方留给状态栏的比例")]
        [Range(0f, 0.3f)] public float topMargin = 0.08f;
        [Range(0f, 0.3f)] public float bottomMargin = 0.03f;
        [Range(0f, 0.3f)] public float sideMargin = 0.02f;

        [Header("Player orbit (right-drag)")]
        [Tooltip("玩家最多往左、往右各转多少度")]
        [Range(0f, 180f)] public float maxOrbitYaw = 90f;
        [Tooltip("玩家转动时俯仰角的范围（90 = 正上方往下看）")]
        public Vector2 orbitPitchRange = new Vector2(25f, 80f);
        [Tooltip("滚轮缩放的范围，1 = 正常距离，大于 1 拉近")]
        public Vector2 orbitZoomRange = new Vector2(0.7f, 2.5f);

        [Header("Orbit feel")]
        [Tooltip("相机追上鼠标要多久（秒）。越大越“润”，跟手的拖尾越明显；0 = 完全跟手")]
        [Range(0f, 0.5f)] public float orbitSmoothTime = 0.12f;
        [Tooltip("松手后惯性滑行停下来的快慢，越大停得越快；0 = 没有惯性")]
        [Range(0f, 20f)] public float orbitFriction = 4f;
        [Tooltip("拖到边界外还能再拉多少度（越拉越沉，像橡皮筋），松手后弹回边界")]
        [Range(0f, 30f)] public float orbitOverscroll = 12f;
        [Tooltip("弹回边界的快慢")]
        [Range(1f, 30f)] public float orbitSpringBack = 10f;
        [Tooltip("滚轮缩放追上目标要多久（秒）")]
        [Range(0f, 0.5f)] public float zoomSmoothTime = 0.15f;

        /// <summary>除了平面布局的四个角，还必须框进画面的点（本地坐标），例如第三阶段升起来的城堡顶。改完调用 Fit()。</summary>
        [System.NonSerialized] public List<Vector3> extraPoints = new List<Vector3>();

        /// <summary>玩家在正面视角之上额外转的水平角、俯仰角（度），以及拉近的倍数（现在画面上的值，会慢慢追上目标）。</summary>
        public float OrbitYaw { get; private set; }
        public float OrbitPitch { get; private set; }
        public float OrbitZoom { get; private set; } = 1f;

        // 拖动、滚轮改的是目标值，画面上的值每帧平滑地追上去；松手后目标值带着惯性继续滑一段
        float targetYaw, targetPitch, targetZoom = 1f;
        float yawVelocity, pitchVelocity, zoomVelocity;
        Vector2 inertia; // 度/秒
        bool dragging;

        /// <summary>正面视角的朝向（不含玩家的旋转）。棋子、文字朝着它摆，玩家转视角时它们不动。</summary>
        public Transform ViewReference { get; private set; }

        Camera cam;
        float fittedAspect;
        Vector3 frontPosition;
        Quaternion frontRotation = Quaternion.identity;

        void Awake()
        {
            cam = GetComponent<Camera>();
            var reference = new GameObject("View Reference (not saved)") { hideFlags = HideFlags.DontSave };
            reference.transform.SetParent(transform.parent, false);
            ViewReference = reference.transform;
            Fit();
        }

        void OnDestroy()
        {
            if (ViewReference != null) Destroy(ViewReference.gameObject);
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(cam.aspect, fittedAspect)) Fit();
            UpdateOrbitMotion(Time.unscaledDeltaTime);
        }

        // 运行中在 Inspector 里改参数时立刻重新取景
        void OnValidate()
        {
            var camera = cam != null ? cam : GetComponent<Camera>();
            if (camera != null) camera.fieldOfView = fieldOfView;
            if (Application.isPlaying && cam != null) Fit();
        }

        [ContextMenu("Fit Now")]
        public void Fit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            fittedAspect = cam.aspect;
            cam.fieldOfView = fieldOfView;
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

            frontPosition = transform.localPosition;
            frontRotation = rotation;
            if (ViewReference != null) ViewReference.localRotation = rotation;
            ApplyOrbit();
        }

        /// <summary>
        /// 玩家拖动（每帧调用，鼠标没动也要传 0）：改目标角度；拖到边界外越拉越沉。顺便估计拖动的速度，松手后当惯性用。
        /// </summary>
        public void Drag(float deltaYaw, float deltaPitch)
        {
            dragging = true;
            deltaYaw *= Resistance(targetYaw, -maxOrbitYaw, maxOrbitYaw, deltaYaw);
            deltaPitch *= Resistance(pitch + targetPitch, orbitPitchRange.x, orbitPitchRange.y, deltaPitch);
            targetYaw += deltaYaw;
            targetPitch += deltaPitch;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            inertia = Vector2.Lerp(inertia, new Vector2(deltaYaw, deltaPitch) / dt, 0.5f);
        }

        /// <summary>松开右键：之后按惯性滑行、弹回边界。</summary>
        public void EndDrag() => dragging = false;

        /// <summary>滚轮：目标缩放乘上 factor。</summary>
        public void Zoom(float factor)
        {
            targetZoom = Mathf.Clamp(targetZoom * factor, orbitZoomRange.x, orbitZoomRange.y);
        }

        /// <summary>往边界外拖时的阻力：刚出界几乎不受影响，出界 orbitOverscroll 度时完全拉不动。往回拖不受影响。</summary>
        float Resistance(float value, float min, float max, float delta)
        {
            float over = delta > 0 ? value - max : min - value;
            if (over <= 0f) return 1f;
            if (orbitOverscroll <= 0f) return 0f;
            return Mathf.Clamp01(1f - over / orbitOverscroll);
        }

        void UpdateOrbitMotion(float dt)
        {
            if (dt <= 0f) return;
            if (!dragging)
            {
                // 惯性滑行，按指数衰减
                if (orbitFriction > 0f && inertia.sqrMagnitude > 0.01f)
                {
                    targetYaw += inertia.x * dt;
                    targetPitch += inertia.y * dt;
                    inertia *= Mathf.Exp(-orbitFriction * dt);
                }
                else inertia = Vector2.zero;

                // 出界了就弹回边界（同时停掉往外的惯性）
                float spring = 1f - Mathf.Exp(-orbitSpringBack * dt);
                float clampedYaw = Mathf.Clamp(targetYaw, -maxOrbitYaw, maxOrbitYaw);
                if (!Mathf.Approximately(clampedYaw, targetYaw))
                {
                    targetYaw = Mathf.Lerp(targetYaw, clampedYaw, spring);
                    inertia.x = 0f;
                }
                float clampedPitch = Mathf.Clamp(pitch + targetPitch, orbitPitchRange.x, orbitPitchRange.y) - pitch;
                if (!Mathf.Approximately(clampedPitch, targetPitch))
                {
                    targetPitch = Mathf.Lerp(targetPitch, clampedPitch, spring);
                    inertia.y = 0f;
                }
            }

            // 画面上的值平滑地追上目标
            if (Mathf.Abs(OrbitYaw - targetYaw) < 0.001f && Mathf.Abs(OrbitPitch - targetPitch) < 0.001f && Mathf.Abs(OrbitZoom - targetZoom) < 0.0001f)
                return;
            OrbitYaw = Mathf.SmoothDamp(OrbitYaw, targetYaw, ref yawVelocity, orbitSmoothTime, Mathf.Infinity, dt);
            OrbitPitch = Mathf.SmoothDamp(OrbitPitch, targetPitch, ref pitchVelocity, orbitSmoothTime, Mathf.Infinity, dt);
            OrbitZoom = Mathf.SmoothDamp(OrbitZoom, targetZoom, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
            ApplyOrbit();
        }

        /// <summary>直接设定玩家的旋转（0, 0, 1 = 正面视角）。</summary>
        /// <summary>直接设定玩家的旋转（0, 0, 1 = 正面视角），立刻生效，停掉惯性。</summary>
        public void SetOrbit(float orbitYaw, float orbitPitch, float orbitZoom)
        {
            OrbitYaw = targetYaw = orbitYaw;
            OrbitPitch = targetPitch = orbitPitch;
            OrbitZoom = targetZoom = orbitZoom;
            yawVelocity = pitchVelocity = zoomVelocity = 0f;
            inertia = Vector2.zero;
            dragging = false;
            ApplyOrbit();
        }

        public bool IsFront => Mathf.Approximately(OrbitYaw, 0f) && Mathf.Approximately(OrbitPitch, 0f) && Mathf.Approximately(OrbitZoom, 1f)
            && Mathf.Approximately(targetYaw, 0f) && Mathf.Approximately(targetPitch, 0f) && Mathf.Approximately(targetZoom, 1f);

        /// <summary>在 duration 秒内平滑转回正面视角。</summary>
        public IEnumerator ReturnToFront(float duration)
        {
            if (IsFront) yield break;
            float y = OrbitYaw, p = OrbitPitch, z = OrbitZoom;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                SetOrbit(Mathf.Lerp(y, 0f, k), Mathf.Lerp(p, 0f, k), Mathf.Lerp(z, 1f, k));
                yield return null;
            }
            SetOrbit(0f, 0f, 1f);
        }

        /// <summary>
        /// 把正面视角绕棋盘中心（Chess 根物体的原点）转 OrbitYaw / OrbitPitch，再按 OrbitZoom 拉近：
        /// 相机和棋盘中心的相对位置一起转，所以旋转为 0 时和正面视角一模一样，转起来棋盘大小也基本不变。
        /// </summary>
        void ApplyOrbit()
        {
            // 允许暂时拉出范围一点（橡皮筋），但不能钻到桌子下面或翻过头顶
            float low = Mathf.Max(1f, Mathf.Min(orbitPitchRange.x, pitch) - orbitOverscroll);
            float high = Mathf.Min(89.9f, Mathf.Max(orbitPitchRange.y, pitch) + orbitOverscroll);
            float orbitPitchNow = Mathf.Clamp(pitch + OrbitPitch, low, high) - pitch;
            var rotation = Quaternion.Euler(pitch + orbitPitchNow, yaw + OrbitYaw, 0f);
            var offset = Quaternion.Inverse(frontRotation) * frontPosition / Mathf.Max(0.01f, OrbitZoom);
            transform.localRotation = rotation;
            transform.localPosition = rotation * offset;
        }

        /// <summary>在 duration 秒内平滑转到新的俯仰角，转的过程中一直保持布局框在屏幕里。</summary>
        public IEnumerator TurnTo(float targetPitch, float duration) => TurnTo(targetPitch, fieldOfView, zoom, duration);

        /// <summary>在 duration 秒内把俯仰角、视野角、缩放一起平滑过渡到新值，过程中一直保持布局框在屏幕里。</summary>
        public IEnumerator TurnTo(float targetPitch, float targetFieldOfView, float targetZoom, float duration)
        {
            float startPitch = pitch, startFov = fieldOfView, startZoom = zoom;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                pitch = Mathf.Lerp(startPitch, targetPitch, k);
                fieldOfView = Mathf.Lerp(startFov, targetFieldOfView, k);
                zoom = Mathf.Lerp(startZoom, targetZoom, k);
                Fit();
                yield return null;
            }
            pitch = targetPitch;
            fieldOfView = targetFieldOfView;
            zoom = targetZoom;
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
