using UnityEngine;

/// <summary>
/// 横版镜头：平滑跟随目标的 X（可选 Y），保持固定的侧面距离。
/// </summary>
public class SideScrollCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 1.2f, -10f);
    public float smoothTime = 0.2f;
    public bool followY = false;

    Vector3 velocity;

    void Start()
    {
        if (target != null)
            transform.position = GetDesiredPosition();
    }

    void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position, GetDesiredPosition(), ref velocity, smoothTime);
    }

    Vector3 GetDesiredPosition()
    {
        var pos = target.position + offset;
        if (!followY) pos.y = offset.y;
        return pos;
    }
}
