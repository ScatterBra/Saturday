using UnityEngine;

/// <summary>
/// 斜视角跟随镜头（类似 Hades）：固定俯仰角和水平转角，从远处用小 FOV 看向目标，平滑跟随。
/// </summary>
public class IsometricFollowCamera : MonoBehaviour
{
    public Transform target;
    [Range(10f, 89f)] public float pitch = 45f;
    public float yaw = 45f;
    public float distance = 28f;
    public float fieldOfView = 22f;
    [Tooltip("看向目标上方的高度（大约角色胸口）")]
    public float lookHeight = 1f;
    public float smoothTime = 0.15f;

    Vector3 velocity;
    Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (cam != null) cam.fieldOfView = fieldOfView;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = Vector3.SmoothDamp(transform.position, GetDesiredPosition(), ref velocity, smoothTime);
    }

    public void SnapToTarget()
    {
        if (target == null) return;
        if (cam == null) cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = fieldOfView;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = GetDesiredPosition();
    }

    Vector3 GetDesiredPosition()
    {
        var focus = target.position + Vector3.up * lookHeight;
        return focus - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * distance;
    }
}
