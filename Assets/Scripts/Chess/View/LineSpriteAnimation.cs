using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 沿一个方向播一遍帧动画（例如消防栓喷水），播完自己删掉。
    /// 图片的 X 轴沿着 direction 摆，并绕这条线转到正对相机：从哪个角度看都是一道立在空中的水柱，不会变成一条细线。
    /// 帧图的 pivot 设在左边中间，物体的位置就是图片左端（喷水的起点）。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class LineSpriteAnimation : MonoBehaviour
    {
        Sprite[] frames;
        float frameTime;
        Vector3 direction;
        Camera viewCamera;
        SpriteRenderer spriteRenderer;
        float time;

        public static LineSpriteAnimation Play(Transform parent, Vector3 localStart, Vector3 worldDirection, Sprite[] frames, float fps, Camera camera)
        {
            var go = new GameObject("Line Animation");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localStart;
            go.AddComponent<SpriteRenderer>();
            var animation = go.AddComponent<LineSpriteAnimation>();
            animation.frames = frames;
            animation.frameTime = 1f / Mathf.Max(0.01f, fps);
            animation.direction = worldDirection.sqrMagnitude > 0f ? worldDirection.normalized : Vector3.right;
            animation.viewCamera = camera;
            animation.spriteRenderer = go.GetComponent<SpriteRenderer>();
            animation.spriteRenderer.sprite = frames[0];
            animation.Face();
            return animation;
        }

        void LateUpdate()
        {
            time += Time.deltaTime;
            int frame = Mathf.FloorToInt(time / frameTime);
            if (frame >= frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            spriteRenderer.sprite = frames[frame];
            Face();
        }

        /// <summary>X 轴沿喷水方向，图片平面绕这条线转到正对相机。</summary>
        void Face()
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3 toCamera = cam != null ? cam.transform.position - transform.position : Vector3.up;
            var normal = toCamera - direction * Vector3.Dot(toCamera, direction);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();
            transform.rotation = Quaternion.LookRotation(normal, Vector3.Cross(normal, direction));
        }
    }
}
