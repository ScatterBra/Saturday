using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>表现层在编辑器预览（非 Play 模式）和游戏里都要用的小工具。</summary>
    public static class ViewUtil
    {
        /// <summary>Play 模式下用 Destroy，编辑器预览里只能用 DestroyImmediate。</summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
