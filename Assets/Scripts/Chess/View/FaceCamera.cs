using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>让棋子贴图、3D 文字始终和相机朝向一致（相机的俯仰角、水平转角改了也跟着转）。</summary>
    public class FaceCamera : MonoBehaviour
    {
        public Transform target;

        void LateUpdate()
        {
            if (target != null) transform.rotation = target.rotation;
        }
    }
}
