using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 第三阶段的地形，在 Inspector 里直接用字符网格编辑（格式见 Stage3Layout 的说明）。
    /// 棋子不在这里摆：风暴把双方的棋子随机吹散，岩浆也是随机撒的（ChessGameController 的 lavaSquares）。
    /// 第一行是第 8 行（对手那边），最后一行是第 1 行（玩家这边），每行从 a 列到 h 列。
    /// </summary>
    [CreateAssetMenu(menuName = "Saturday/Chess Stage 3 Layout", fileName = "Stage3Layout")]
    public class Stage3LayoutAsset : ScriptableObject
    {
        [TextArea(8, 9)]
        [Tooltip(". 平地  1-9 高原高度  C 城堡  ~ 固定的岩浆（一般不用，岩浆是随机撒的）")]
        public string terrain = Stage3Layout.DefaultTerrain;

        /// <summary>解析成逻辑层用的布局；格式不对时在 Console 报错并返回 null（游戏会改用默认布局）。</summary>
        public Stage3Layout Build()
        {
            try
            {
                return Stage3Layout.Parse(terrain);
            }
            catch (System.FormatException e)
            {
                Debug.LogError($"Stage 3 layout '{name}' is invalid, using the default layout instead: {e.Message}", this);
                return null;
            }
        }
    }
}
