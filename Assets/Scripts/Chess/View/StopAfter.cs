using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>过 seconds 秒后停掉身上所有粒子（不清掉已经发出去的），用于播一段就停的循环特效（例如下雨）。</summary>
    public sealed class StopAfter : MonoBehaviour
    {
        public float seconds = 2f;
        float time;

        void Update()
        {
            time += Time.deltaTime;
            if (time < seconds) return;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            enabled = false;
        }
    }
}
