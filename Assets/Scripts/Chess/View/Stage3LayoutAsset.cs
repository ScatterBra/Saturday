using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 第三阶段的地形和阵型，在 Inspector 里直接用字符网格编辑（格式见 Stage3Layout 的说明）。
    /// 第一行是第 8 行（对手那边），最后一行是第 1 行（玩家这边），每行从 a 列到 h 列。
    /// </summary>
    [CreateAssetMenu(menuName = "Saturday/Chess Stage 3 Layout", fileName = "Stage3Layout")]
    public class Stage3LayoutAsset : ScriptableObject
    {
        [TextArea(8, 9)]
        [Tooltip("_ 我方一侧平地  . 对面平地  1-9 高原高度  C 城堡  ~ 岩浆")]
        public string terrain = Stage3Layout.DefaultTerrain;

        [TextArea(8, 9)]
        [Tooltip("p n b r q k 朋友的棋子  K 我方王  N 我方马  * 我方其他棋子  . 空")]
        public string pieces = Stage3Layout.DefaultPieces;

        /// <summary>解析成逻辑层用的布局；格式不对时在 Console 报错并返回 null（游戏会改用默认布局）。</summary>
        public Stage3Layout Build()
        {
            try
            {
                return Stage3Layout.Parse(terrain, pieces);
            }
            catch (System.FormatException e)
            {
                Debug.LogError($"Stage 3 layout '{name}' is invalid, using the default layout instead: {e.Message}", this);
                return null;
            }
        }
    }
}
