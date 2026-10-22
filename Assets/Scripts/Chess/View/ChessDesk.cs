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
        [Tooltip("棋盘图（SpriteRenderer）。有图时平地格子藏在图下面，高亮改成盖在图上的半透明色块")]
        public SpriteRenderer board;
        [Tooltip("棋盘图里已经画了 a–h、1–8，勾上后不再显示程序生成的坐标字")]
        public bool boardHasCoordinates = true;

        [Tooltip("左边 NEW 区：规则卡和提示字挂在这个物体下面（420 × 900 像素 = 4.2 × 9 格）。它（或子物体）上有 SpriteRenderer 就当作底图，没有就把字直接写在桌上")]
        public Transform newArea;
        [Tooltip("右边规则书：规则文字和被吃区挂在这个物体下面（4.2 × 9 格）。子物体上的 SpriteRenderer 是本子的底图。这个物体本身不要旋转（字的画布会自己平躺），要转方向就转子物体")]
        public Transform ruleBook;

        [Tooltip("规则卡底图，每次弹卡随机挑（同一组卡不重复），张数不限")]
        public Sprite[] ruleCards = new Sprite[0];

        /// <summary>这个区域有没有铺底图。</summary>
        public static bool HasArt(Transform area)
        {
            if (area == null) return false;
            var sprite = area.GetComponentInChildren<SpriteRenderer>();
            return sprite != null && sprite.sprite != null && sprite.enabled;
        }

        public bool HasBoardArt => board != null && board.sprite != null && board.enabled;
    }
}
