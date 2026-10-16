using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 让斜视相机自动框住整张桌面布局（左边规则卡区、棋盘、右边规则书）。
    /// 俯仰角固定，只调整相机离桌面的距离和前后位置，屏幕比例变了会重新计算。
    /// 挂在棋盘相机上，相机需要是 Chess 根物体的子物体。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DeskCameraFit : MonoBehaviour
    {
        [Tooltip("俯仰角，90 = 正上方往下看")]
        [Range(30f, 90f)] public float pitch = 65f;
        [Tooltip("布局左右、前后的半宽（本地坐标），默认按 ChessHud.DeskLayout 计算")]
        public Vector2 halfExtent = new Vector2(ChessHud.DeskLayout.HalfExtentX, ChessHud.DeskLayout.HalfExtentZ);
        [Tooltip("屏幕上方留给状态栏的比例")]
        [Range(0f, 0.3f)] public float topMargin = 0.08f;
        [Range(0f, 0.3f)] public float bottomMargin = 0.03f;
        [Range(0f, 0.3f)] public float sideMargin = 0.02f;

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

        [ContextMenu("Fit Now")]
        public void Fit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            fittedAspect = cam.aspect;
            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.localRotation = rotation;

            // 二分查找最小距离：在这个距离上，调整注视点前后位置让布局上下居中，再看四个角是否都在边距内
            float near = 1f, far = 200f, focusZ = 0f;
            for (int i = 0; i < 40; i++)
            {
                float distance = (near + far) / 2;
                focusZ = CenterVertically(rotation, distance);
                if (Fits(rotation, distance, focusZ)) far = distance;
                else near = distance;
            }
            focusZ = CenterVertically(rotation, far);
            Place(rotation, far, focusZ);
        }

        float CenterVertically(Quaternion rotation, float distance)
        {
            float focusZ = 0f;
            float wantedMid = (bottomMargin + (1f - topMargin)) / 2;
            for (int i = 0; i < 12; i++)
            {
                Place(rotation, distance, focusZ);
                float farY = Viewport(new Vector3(0, 0, halfExtent.y)).y;
                float nearY = Viewport(new Vector3(0, 0, -halfExtent.y)).y;
                float span = farY - nearY;
                if (span <= 0.0001f) break;
                float mid = (farY + nearY) / 2;
                // 中点偏高就把注视点往远处挪，反之往近处挪
                focusZ += (mid - wantedMid) / span * 2f * halfExtent.y;
            }
            return focusZ;
        }

        bool Fits(Quaternion rotation, float distance, float focusZ)
        {
            Place(rotation, distance, focusZ);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var v = Viewport(new Vector3(sx * halfExtent.x, 0, sz * halfExtent.y));
                    if (v.z <= 0 || v.x < sideMargin || v.x > 1 - sideMargin || v.y < bottomMargin || v.y > 1 - topMargin)
                        return false;
                }
            }
            return true;
        }

        void Place(Quaternion rotation, float distance, float focusZ) =>
            transform.localPosition = new Vector3(0, 0, focusZ) - rotation * Vector3.forward * distance;

        Vector3 Viewport(Vector3 local)
        {
            var parent = transform.parent;
            var world = parent != null ? parent.TransformPoint(local) : local;
            return cam.WorldToViewportPoint(world);
        }
    }
}
