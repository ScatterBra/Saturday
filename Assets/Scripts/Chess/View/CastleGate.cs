using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 城堡模型上的吊桥（Castle.fbx 的子物体 Gate，原点在门底的铰链上）。
    /// 关着是竖直的；打开时绕本地 X 轴转 openAngle 度，平放到门前那一格（b7）上。
    /// </summary>
    public class CastleGate : MonoBehaviour
    {
        [Tooltip("打开时绕本地 X 轴转多少度。-90 = 门顶倒向城堡外面（b7 那边）")]
        public float openAngle = -90f;
        [Tooltip("放下或收起要多久（秒）")]
        public float seconds = 0.8f;

        float angle;
        float target;

        /// <summary>开门或关门。instant = 直接到位，不播动画（换棋盘、跳过过场时）。</summary>
        public void SetOpen(bool open, bool instant)
        {
            target = open ? openAngle : 0f;
            if (instant)
            {
                angle = target;
                Apply();
            }
        }

        void Update()
        {
            if (Mathf.Approximately(angle, target)) return;
            float speed = Mathf.Abs(openAngle) / Mathf.Max(0.01f, seconds);
            // 吊桥落下时越来越快，像是被重力拉下来
            float t = Mathf.InverseLerp(0f, openAngle, angle);
            float boost = target != 0f ? Mathf.Lerp(0.5f, 1.8f, t) : 1f;
            angle = Mathf.MoveTowards(angle, target, speed * boost * Time.deltaTime);
            Apply();
        }

        void Apply() => transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }
}
