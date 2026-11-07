using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 把粒子包（例如 ParticlePack）里的特效 prefab 放到棋盘上用。只改生成出来的实例，不动原 prefab。
    /// </summary>
    public static class ParticleFx
    {
        /// <summary>
        /// 在 parent 下生成一份特效：缩放到 scale，绕竖轴转 yaw 度（保留 prefab 自带的朝向）。
        /// 粒子包里的特效默认只认自己的缩放，这里改成跟着父物体一起缩。
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 localPosition, float scale, float yaw = 0f)
        {
            var fx = Object.Instantiate(prefab, parent);
            fx.transform.localPosition = localPosition;
            fx.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
            // 不管 prefab 根物体原来的缩放（演示场景里各不相同），统一用 scale
            fx.transform.localScale = Vector3.one * scale;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return fx;
        }

        /// <summary>循环的特效快进 seconds 秒：一出现就是烧着的状态，几份之间也不同步。</summary>
        public static void Warm(GameObject fx, float seconds)
        {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(seconds, false, true);
                ps.Play(false);
            }
        }

        /// <summary>只播一次（粒子包里的爆炸默认是循环的），播完自动删掉。</summary>
        public static void PlayOnce(GameObject fx)
        {
            float longest = 0f;
            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.loop = false;
                main.prewarm = false;
                longest = Mathf.Max(longest, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }
            foreach (var ps in systems) ps.Play(false);
            if (Application.isPlaying) Object.Destroy(fx, longest + 0.5f);
        }
    }
}
