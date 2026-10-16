using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 斜视角（类似 Hades）行走：WASD 八方向移动，方向相对于镜头朝向，角色转向移动方向。
/// 走路动作由 ProceduralWalk 组件负责。
/// </summary>
public class TopDownWalker : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float acceleration = 30f;
    public float turnSpeed = 900f;
    [Tooltip("移动方向参照的镜头，留空则使用 Camera.main")]
    public Transform cameraTransform;
    [Tooltip("可行走区域的半边长（以原点为中心的正方形）")]
    public float boundsHalfExtent = 24f;

    Vector3 velocity;

    void Update()
    {
        Vector2 input = Vector2.zero;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
        }
        input = Vector2.ClampMagnitude(input, 1f);

        var cam = cameraTransform != null ? cameraTransform : (Camera.main != null ? Camera.main.transform : null);
        float cameraYaw = cam != null ? cam.eulerAngles.y : 0f;
        Vector3 moveDir = Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(input.x, 0f, input.y);

        velocity = Vector3.MoveTowards(velocity, moveDir * moveSpeed, acceleration * Time.deltaTime);

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            var targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

        var pos = transform.position + velocity * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, -boundsHalfExtent, boundsHalfExtent);
        pos.z = Mathf.Clamp(pos.z, -boundsHalfExtent, boundsHalfExtent);
        transform.position = pos;
    }
}
