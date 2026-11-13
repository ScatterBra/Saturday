using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 场景里摆好的桌面（Chess/Desk）：背景、棋盘图、NEW 区和规则书的位置，可以在 Scene 视图里直接拖动、缩放。
    /// 运行时 ChessGameController 读这里：棋盘图在不在、坐标字要不要画，NEW 区和规则书的文字挂在对应的物体下面，
    /// 规则卡从 ruleCards 里随机挑底图。棋子、高亮、规则卡、文字仍是运行时生成的。
    /// 棋盘图要盖住 9 × 9 格（中间 8 × 8 是格子，四周各半格边框），中心对准 Chess 原点，否则点击位置会和看到的格子错开。
    /// </summary>
    public class ChessDesk : MonoBehaviour
    {
        [Tooltip("棋盘（有厚度的模型 MeshRenderer，或平面的 SpriteRenderer），顶面要在 y ≈ 0。有它时平地格子藏在下面，高亮改成盖在顶面上的半透明色块")]
        public Renderer board;
        [Tooltip("棋盘图里已经画了 a–h、1–8，勾上后不再显示程序生成的坐标字")]
        public bool boardHasCoordinates = true;

        [Tooltip("左边 NEW 区：规则卡和提示字挂在这个物体下面（420 × 900 像素 = 4.2 × 9 格）。它（或子物体）上有 SpriteRenderer 就当作底图，没有就把字直接写在桌上")]
        public Transform newArea;
        [Tooltip("右边规则书：规则文字和被吃区挂在这个物体下面（4.2 × 9 格）。子物体上的模型或 SpriteRenderer 是本子的底图，物体本身放在本子顶面的高度。这个物体本身不要旋转（字的画布会自己平躺），要转方向就转子物体")]
        public Transform ruleBook;

        [Tooltip("每格岩浆上放一份的特效（例如 ParticlePack 的 WildFire），留空就只有橙色色块")]
        public GameObject lavaEffect;
        [Tooltip("岩浆特效的整体缩放。WildFire 原大约 3.6 格宽，0.3 左右刚好盖住一格")]
        public float lavaEffectScale = 0.3f;
        [Tooltip("岩浆特效往上抬多少（世界单位）。WildFire 的火苗从地面以下长出来，不抬会被棋盘顶面挡住一大半")]
        public float lavaEffectLift = 0.2f;

        [Tooltip("第三阶段盖在整个棋盘上空的雷雨云（例如 ParticlePack 的 LightnigStormCloud），留空就没有")]
        public GameObject stormCloud;
        [Tooltip("雷雨云比最高的地形（城堡和 Princess 字样）再高多少（世界单位）。相机大约在 17 高，云要比它低")]
        public float stormCloudClearance = 1.5f;
        [Tooltip("雷雨云铺开的边长（世界单位）。棋盘是 9 × 9")]
        public float stormCloudSize = 10f;
        [Tooltip("云朵粒子比原特效大几倍（铺得开、粒子数不会太多）")]
        public float stormCloudPuffScale = 2f;
        [Tooltip("云朵的不透明度（0–1）。云挡在相机和棋盘之间，太浓会看不清棋子")]
        [Range(0f, 1f)] public float stormCloudOpacity = 0.3f;
        [Tooltip("云量：1 = 按面积铺满；小一点云会稀一些，露出更多棋盘")]
        [Range(0f, 2f)] public float stormCloudDensity = 0.5f;

        [Tooltip("棋子被烧掉（火龙、掉进岩浆）时换上的溶解材质（Saturday/Chess Dissolve），留空就用原来的橙色球 + 缩小")]
        public Material dissolveMaterial;
        [Tooltip("溶解要多久（秒）")]
        public float dissolveDuration = 1.2f;
        [Tooltip("溶解时在棋子上播一次的粒子（例如 ParticlePack 的 Dissolve，里面演示用的球会自动关掉），留空就不播")]
        public GameObject dissolveParticles;
        [Tooltip("溶解粒子的整体缩放")]
        public float dissolveParticlesScale = 1f;

        [Tooltip("消防栓喷水的帧动画（由 water.gif 拆出来，图片左端是喷水口），和喷水音效同时播；留空就没有")]
        public Sprite[] hydrantSprayFrames = new Sprite[0];
        [Tooltip("喷水动画每秒几帧（water.gif 是每帧 0.08 秒 = 12.5）")]
        public float hydrantSprayFps = 12.5f;
        [Tooltip("喷水口离棋盘多高（世界单位）")]
        public float hydrantSprayHeight = 0.35f;
        [Tooltip("水柱的起点离消防栓多远（沿喷水方向，世界单位）；0 = 从消防栓正中间喷出")]
        public float hydrantSprayOffset = 0.3f;

        [Tooltip("第三阶段升起的地块：普通方块四个侧面轮流贴这几张（例如 Grid_1、Grid_1.5）。每张图对应 1 × 1 的侧面，顶边是草。留空就是原来的纯色方块")]
        public Texture2D[] raisedSideTextures = new Texture2D[0];
        [Tooltip("骷髅方块的一个侧面贴这张（例如 Grid_2），其他三面和普通方块一样")]
        public Texture2D raisedSkullTexture;
        [Tooltip("每个方块是骷髅方块的机会（0–1），小一点骷髅就少")]
        [Range(0f, 1f)] public float raisedSkullChance = 0.15f;

        [Tooltip("兵变成树、树变回兵（或被吃掉）时在那一格播一次的特效（例如 ParticlePack 的 DustExplosion），留空就不播")]
        public GameObject treeEffect;
        [Tooltip("树特效的整体缩放")]
        public float treeEffectScale = 0.4f;

        [Header("Stage 3 skills")]
        [Tooltip("潮湿的棋子遇火爆炸、自爆时在爆炸中心播一次的特效（例如 ParticlePack 的 SmallExplosion），留空就不播")]
        public GameObject explosionEffect;
        [Tooltip("爆炸特效的整体缩放")]
        public float explosionEffectScale = 0.5f;
        [Tooltip("下雨技能：在棋盘上空额外播一段的特效（例如 ParticlePack 的 RainEffect），留空就不播。雷雨云自己的雨平时关着，有棋子潮湿时才下")]
        public GameObject rainEffect;
        [Tooltip("下雨特效的整体缩放")]
        public float rainEffectScale = 0.5f;
        [Tooltip("下雨特效播多久（秒）")]
        public float rainDuration = 2.5f;
        [Tooltip("雷暴大招：4 × 4 范围里每一格播一次的特效（例如 ParticlePack 的 ElectricalSparks），留空就只闪光")]
        public GameObject thunderEffect;
        [Tooltip("雷暴特效的整体缩放")]
        public float thunderEffectScale = 0.5f;
        [Tooltip("象的光束：沿斜线一路裂开的特效（例如 ParticlePack 的 EarthShatter），留空就用一根发光的细条")]
        public GameObject beamEffect;
        [Tooltip("光束特效的整体缩放")]
        public float beamEffectScale = 0.35f;
        [Tooltip("光束特效从象走到斜线尽头用多久（秒）")]
        public float beamEffectTravel = 0.6f;
        [Tooltip("没有光束特效时，发光细条的颜色（HDR，越亮越刺眼）")]
        [ColorUsage(false, true)] public Color beamColor = new Color(2.5f, 2.2f, 0.9f);
        [Tooltip("潮湿的棋子染上的颜色")]
        public Color wetTint = new Color(0.6f, 0.78f, 1f);

        [Tooltip("规则卡底图，每次弹卡随机挑（同一组卡不重复），张数不限")]
        public Sprite[] ruleCards = new Sprite[0];

        /// <summary>这个区域有没有铺底图（模型或图片都算）。</summary>
        public static bool HasArt(Transform area)
        {
            if (area == null) return false;
            foreach (var r in area.GetComponentsInChildren<Renderer>())
                if (Shows(r)) return true;
            return false;
        }

        public bool HasBoardArt => Shows(board);

        static bool Shows(Renderer r)
        {
            if (r == null || !r.enabled) return false;
            if (r is SpriteRenderer sprite) return sprite.sprite != null;
            return r is MeshRenderer;
        }
    }
}
