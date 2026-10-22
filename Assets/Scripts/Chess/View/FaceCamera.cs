using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 让棋子贴图、3D 文字朝着相机：跟着相机的水平转角转，但俯仰角固定在 pitch，
    /// 所以游戏中途相机倾斜时（例如选了树技能）2D 棋子还是贴在棋盘上，不跟着倾斜。
    /// 第三阶段（FollowCameraPitch = true）改成完全朝着相机，棋子随着相机的角度立起来。
    /// </summary>
    public class FaceCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("固定的俯仰角，90 = 平躺在棋盘上")]
        public float pitch = 90f;

        /// <summary>所有棋子、文字都完全朝着相机（第三阶段打开，开新局时关掉）。</summary>
        public static bool FollowCameraPitch;

        void LateUpdate()
        {
            if (target == null) return;
            transform.rotation = FollowCameraPitch ? target.rotation : Rotation(target, pitch);
        }

        /// <summary>按相机的水平转角、给定的俯仰角算出朝向。</summary>
        public static Quaternion Rotation(Transform camera, float pitch)
        {
            // 正俯视时 forward 是竖直向下的，水平方向改从相机的 up 取
            var flat = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(camera.up, Vector3.up);
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            return Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>相机现在往下看的角度（90 = 正俯视）。</summary>
        public static float PitchOf(Transform camera) => Mathf.Asin(Mathf.Clamp(-camera.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
