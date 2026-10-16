using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2D 平面行走：A/D 控制左右移动，角色沿 X 轴移动并转身朝向移动方向。
/// 走路动作由 ProceduralWalk 组件负责。
/// </summary>
public class SideScrollWalker : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float acceleration = 20f;
    public float turnSpeed = 720f;
    public float minX = -95f;
    public float maxX = 95f;

    float velocityX;
    float facingYaw = 90f;

    void Update()
    {
        float input = 0f;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input -= 1f;
            if (keyboard.dKey.isPressed) input += 1f;
        }

        velocityX = Mathf.MoveTowards(velocityX, input * moveSpeed, acceleration * Time.deltaTime);

        if (input != 0f)
            facingYaw = input > 0f ? 90f : -90f;

        var targetRotation = Quaternion.Euler(0f, facingYaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);

        var pos = transform.position;
        pos.x += velocityX * Time.deltaTime;
        if (pos.x <= minX || pos.x >= maxX)
        {
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            velocityX = 0f;
        }
        pos.z = 0f;
        transform.position = pos;
    }
}
