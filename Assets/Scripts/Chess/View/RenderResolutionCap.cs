using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 高分屏（Mac Retina、4K 显示器）上 3D 画面按原生分辨率渲染太重（MSAA、SSAO、软阴影都按像素算），
    /// 所以 3D 部分最多按 MaxRenderHeight 行渲染再放大；屏幕空间的 HUD 文字不受影响，仍然是原生分辨率。
    /// 同时打开垂直同步，帧间隔更均匀。只在打包出来的游戏里生效（编辑器里改 URP 资源会被存进项目）。
    /// </summary>
    public class RenderResolutionCap : MonoBehaviour
    {
        const int MaxRenderHeight = 1440;

        UniversalRenderPipelineAsset pipeline;
        int lastHeight = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isEditor) return;
            var go = new GameObject(nameof(RenderResolutionCap));
            DontDestroyOnLoad(go);
            go.AddComponent<RenderResolutionCap>();
        }

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }

        // 玩家切全屏 / 窗口、拖动窗口大小后分辨率会变，变了就重新算
        void Update()
        {
            if (pipeline == null || Screen.height == lastHeight) return;
            lastHeight = Screen.height;
            pipeline.renderScale = lastHeight > MaxRenderHeight ? (float)MaxRenderHeight / lastHeight : 1f;
        }
    }
}
