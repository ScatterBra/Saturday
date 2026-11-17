using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 规则书被吃区里可以点的纸片棋子（复活技能等待点选时）：鼠标停上去描一圈蓝色轮廓，点一下就复活它。
    /// 鼠标停在它上面时 PointerOver = true，规则书就不描黄色轮廓、点击也不会把规则书拿起来。
    /// </summary>
    public class LostPieceHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>鼠标正停在某个可以点的纸片棋子上。</summary>
        public static bool PointerOver => hovered != null;
        static LostPieceHover hovered;

        Outline outline;

        void Awake()
        {
            outline = GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = this;
            if (outline != null) outline.enabled = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hovered == this) hovered = null;
            if (outline != null) outline.enabled = false;
        }

        void OnDisable()
        {
            if (hovered == this) hovered = null;
        }
    }
}
