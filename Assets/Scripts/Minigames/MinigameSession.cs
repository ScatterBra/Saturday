using System;

namespace SaturdayGame.Minigames
{
    /// <summary>
    /// 小游戏和宿主场景（办公室）之间的接口。小游戏结束时调用 Finish，MinigameLauncher 负责卸载场景并恢复办公室。
    /// </summary>
    public static class MinigameSession
    {
        /// <summary>当前小游戏是否由 MinigameLauncher 从办公室里启动。单独打开小游戏场景时为 false。</summary>
        public static bool IsHosted { get; internal set; }

        public static event Action<string> Finished;

        public static void Finish(string result) => Finished?.Invoke(result);
    }
}
