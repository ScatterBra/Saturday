using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 呼吸灯：平贴在棋子脚下的一圈柔光，亮度和大小随时间慢慢一涨一落。
    /// 第三阶段能放技能的象 / 马身上用它提示玩家（BoardView.SetSkillReady 生成）。
    /// </summary>
    public class BreathingGlow : MonoBehaviour
    {
        [Tooltip("一呼一吸多少秒")]
        public float period = 1.6f;
        [Range(0f, 1f)] public float minAlpha = 0.25f;
        [Range(0f, 1f)] public float maxAlpha = 0.9f;
        [Tooltip("最亮时比最暗时大多少（0.1 = 大 10%）")]
        public float grow = 0.1f;

        SpriteRenderer sprite;
        Color color;
        Vector3 baseScale;
        float phase;

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
            color = sprite != null ? sprite.color : Color.white;
            baseScale = transform.localScale;
            // 几枚棋子各自错开一点，不会整齐地一起闪
            phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            float t = (Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, period) + phase) + 1f) * 0.5f;
            if (sprite != null)
            {
                var c = color;
                c.a = color.a * Mathf.Lerp(minAlpha, maxAlpha, t);
                sprite.color = c;
            }
            transform.localScale = baseScale * (1f + grow * t);
        }

        static Sprite glowSprite;

        /// <summary>一圈柔光的贴图（运行时生成）：中间淡、边上一圈亮、往外渐隐。</summary>
        public static Sprite GlowSprite()
        {
            if (glowSprite != null) return glowSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 一圈亮环（在 0.72 处最亮）+ 里面一层淡淡的底光，到边缘为 0
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.13f, 2f));
                    float fill = 0.3f * Mathf.Clamp01(1f - r / 0.8f);
                    float a = Mathf.Clamp01(ring + fill) * Mathf.Clamp01((1f - r) / 0.1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            glowSprite.hideFlags = HideFlags.DontSave;
            return glowSprite;
        }
    }
}
