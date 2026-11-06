using System;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋盘小游戏的音效。挂在 Chess 物体上，ChessGameController 演出事件时调用。
    /// 全部是不分左右的 2D 声音；每个音效可以配好几个文件，随机挑一个播（不会连着两次同一个）。
    /// </summary>
    public class ChessSounds : MonoBehaviour
    {
        [Serializable]
        public class Sfx
        {
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0f, 1f)] public float volume = 1f;
            [Tooltip("每次随机把音高上下浮动这么多（0.05 = ±5%），听起来不机械")]
            [Range(0f, 0.3f)] public float pitchJitter;

            [NonSerialized] public int last = -1;
            /// <summary>每个音效用自己的 AudioSource，改音高不会影响正在播的别的音效。</summary>
            [NonSerialized] public AudioSource source;
        }

        [Range(0f, 1f)] public float masterVolume = 1f;
        [Tooltip("棋子落到格子上（双方走棋、吃子、复活）。不包括被喷开、被拉过来、被击飞")]
        public Sfx pieceFall = new Sfx { pitchJitter = 0.05f };
        [Tooltip("棋子换外观（主教变体、车变消防栓或青蛙等）")]
        public Sfx pieceChange = new Sfx();
        [Tooltip("一枚棋子变成树（每棵树冒出来时各播一次）")]
        public Sfx tree = new Sfx();
        [Tooltip("一组规则卡出现（整组播一次）")]
        public Sfx paperPopup = new Sfx();
        [Tooltip("消防栓喷水")]
        public Sfx hydrant = new Sfx();
        [Tooltip("青蛙把棋子拉过来")]
        public Sfx frog = new Sfx();
        [Tooltip("相机转到新视角（中间视角、第三阶段视角）")]
        public Sfx cameraChange = new Sfx();

        public void Play(Sfx sfx)
        {
            if (sfx == null || sfx.clips == null || sfx.clips.Length == 0) return;
            if (sfx.source == null)
            {
                sfx.source = gameObject.AddComponent<AudioSource>();
                sfx.source.playOnAwake = false;
                sfx.source.spatialBlend = 0f;
            }
            int index = UnityEngine.Random.Range(0, sfx.clips.Length);
            if (sfx.clips.Length > 1 && index == sfx.last) index = (index + 1 + UnityEngine.Random.Range(0, sfx.clips.Length - 1)) % sfx.clips.Length;
            sfx.last = index;
            var clip = sfx.clips[index];
            if (clip == null) return;
            // PlayOneShot 的音高跟着 AudioSource 走，所以每次播之前设一下
            sfx.source.pitch = 1f + UnityEngine.Random.Range(-sfx.pitchJitter, sfx.pitchJitter);
            sfx.source.PlayOneShot(clip, sfx.volume * masterVolume);
        }
    }
}
