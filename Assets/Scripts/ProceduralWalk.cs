using UnityEngine;

/// <summary>
/// 程序化走路动画：根据角色水平移动速度摆动腿和手臂，并让臀部轻微起伏。
/// 素材没有动画片段时使用；挂在带 Humanoid Animator 的角色根节点上。
/// </summary>
[RequireComponent(typeof(Animator))]
public class ProceduralWalk : MonoBehaviour
{
    [Tooltip("达到此速度时摆动幅度最大")]
    public float referenceSpeed = 3f;
    public float strideAngle = 30f;
    public float strideFrequency = 1.6f;
    public float armDownAngle = 70f;
    public float bobHeight = 0.04f;

    float walkPhase;
    float speed01;
    Vector3 lastPosition;

    Transform hips, leftUpLeg, rightUpLeg, leftArm, rightArm;
    Vector3 hipsRestPos;
    Quaternion leftUpLegRest, rightUpLegRest, leftArmRest, rightArmRest;

    void Awake()
    {
        var animator = GetComponent<Animator>();
        if (animator.isHuman)
        {
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            leftUpLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        if (hips) hipsRestPos = hips.localPosition;
        if (leftUpLeg) leftUpLegRest = leftUpLeg.localRotation;
        if (rightUpLeg) rightUpLegRest = rightUpLeg.localRotation;
        if (leftArm) leftArmRest = leftArm.localRotation;
        if (rightArm) rightArmRest = rightArm.localRotation;
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        var delta = transform.position - lastPosition;
        lastPosition = transform.position;
        delta.y = 0f;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        speed01 = Mathf.MoveTowards(speed01, Mathf.Clamp01(speed / referenceSpeed), Time.deltaTime * 8f);

        if (speed01 > 0.01f)
            walkPhase += Time.deltaTime * strideFrequency * Mathf.PI * 2f * speed01;
        else
            walkPhase = Mathf.MoveTowards(walkPhase, Mathf.Round(walkPhase / Mathf.PI) * Mathf.PI, Time.deltaTime * 4f);

        float swing = Mathf.Sin(walkPhase) * strideAngle * speed01;
        Vector3 side = transform.right;
        Vector3 forward = transform.forward;

        if (hips)
        {
            hips.localPosition = hipsRestPos;
            hips.position += Vector3.up * (Mathf.Abs(Mathf.Sin(walkPhase)) * bobHeight * speed01);
        }
        if (leftUpLeg)
        {
            leftUpLeg.localRotation = leftUpLegRest;
            leftUpLeg.rotation = Quaternion.AngleAxis(swing, side) * leftUpLeg.rotation;
        }
        if (rightUpLeg)
        {
            rightUpLeg.localRotation = rightUpLegRest;
            rightUpLeg.rotation = Quaternion.AngleAxis(-swing, side) * rightUpLeg.rotation;
        }
        // 手臂从 T-pose 放下，并与腿反向摆动
        if (leftArm)
        {
            leftArm.localRotation = leftArmRest;
            leftArm.rotation = Quaternion.AngleAxis(-swing, side) * Quaternion.AngleAxis(armDownAngle, forward) * leftArm.rotation;
        }
        if (rightArm)
        {
            rightArm.localRotation = rightArmRest;
            rightArm.rotation = Quaternion.AngleAxis(swing, side) * Quaternion.AngleAxis(-armDownAngle, forward) * rightArm.rotation;
        }
    }
}
