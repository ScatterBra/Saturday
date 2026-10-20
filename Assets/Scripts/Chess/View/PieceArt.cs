using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 棋子贴图配置。没填的棋子类型继续用占位圆盘 + 字母显示。
    /// 黑方贴图留空时用白方贴图乘上 blackTint 来区分两方。
    /// 外观（规则给的 Skin 和冻结外观）在 skins 里按外观 ID 配图，没配图的外观继续用彩色圆环表示。
    /// </summary>
    [CreateAssetMenu(menuName = "Saturday/Chess Piece Art", fileName = "ChessPieceArt")]
    public class PieceArt : ScriptableObject
    {
        /// <summary>一个外观 ID 对应的贴图。可以有多张，按棋子编号固定选一张（例如几种不同的树）。</summary>
        [Serializable]
        public class SkinArt
        {
            [Tooltip("外观 ID，和规则代码里的一致，例如 hydrant、tree、crayon")]
            public string skin;
            [Tooltip("勾选后只对 type 这种棋子生效（例如蜡笔外观的兵、马各用各的图）")]
            public bool onlyForType;
            public PieceType type;
            public Sprite[] white = Array.Empty<Sprite>();
            [Tooltip("留空时黑方用白方的图乘上 variantBlackTint")]
            public Sprite[] black = Array.Empty<Sprite>();
            [Tooltip("立起来，和棋盘垂直（相机倾斜时像立牌一样站着），例如树")]
            public bool standUp;
            [Tooltip("3D 模型（FBX 或预制体）。配了模型就在棋盘上用模型代替贴图（贴图仍用在被吃区等 2D 界面里），多个模型按棋子编号固定选一个")]
            public GameObject[] models = Array.Empty<GameObject>();
        }

        /// <summary>选出来的贴图和它代表的东西，PieceView 据此决定还要不要显示占位标记。</summary>
        public struct Resolved
        {
            public Sprite Sprite;
            /// <summary>不为 null 时棋盘上用这个 3D 模型代替贴图。</summary>
            public GameObject Model;
            /// <summary>贴图立起来，和棋盘垂直。</summary>
            public bool StandUp;
            public Color Tint;
            /// <summary>贴图已经表现了 Skin，不用再画彩色圆环。</summary>
            public bool ShowsSkin;
            /// <summary>贴图已经表现了冻结外观（例如树），不用再画冻结方块。</summary>
            public bool ShowsFrozen;
        }

        [Header("White (default) sprites")]
        public Sprite pawn;
        public Sprite knight;
        public Sprite bishop;
        public Sprite rook;
        public Sprite queen;
        public Sprite king;

        [Header("Black sprites (optional)")]
        public Sprite blackPawn;
        public Sprite blackKnight;
        public Sprite blackBishop;
        public Sprite blackRook;
        public Sprite blackQueen;
        public Sprite blackKing;

        [Tooltip("黑方没有单独贴图时，白方贴图乘上这个颜色")]
        public Color blackTint = new Color(0.42f, 0.42f, 0.48f);
        [Tooltip("外观贴图没有黑方版本时乘上这个颜色（彩色的图压得太暗会看不清，所以比 blackTint 浅）")]
        public Color variantBlackTint = new Color(0.75f, 0.75f, 0.8f);
        [Tooltip("棋子贴图在棋盘上的高度（一格 = 1）")]
        public float pieceHeight = 0.95f;
        [Tooltip("勾了 standUp 的贴图（例如树）和棋盘的夹角，90 = 垂直，越小越往后仰（相机倾斜时显得更高）")]
        [Range(0f, 90f)] public float standAngle = 90f;
        [Tooltip("3D 模型在棋盘上的高度（一格 = 1），按模型包围盒自动缩放")]
        public float modelHeight = 1.1f;
        [Tooltip("3D 模型最宽不超过这么多（一格 = 1）")]
        public float modelWidth = 0.95f;

        [Header("Skins")]
        public List<SkinArt> skins = new List<SkinArt>();

        /// <summary>按优先级选图：冻结外观（例如树）> 规则外观（例如消防栓）> 基础棋子。</summary>
        public Resolved Resolve(Piece piece)
        {
            var result = new Resolved();
            if (piece.IsFrozen && TrySkin(piece.FrozenSkin, piece, ref result))
            {
                result.ShowsFrozen = true;
                return result;
            }
            if (TrySkin(piece.Skin, piece, ref result))
            {
                result.ShowsSkin = true;
                return result;
            }

            var black = piece.Side == Side.Black
                ? Pick(piece.Type, blackPawn, blackKnight, blackBishop, blackRook, blackQueen, blackKing)
                : null;
            result.Sprite = black != null ? black : Pick(piece.Type, pawn, knight, bishop, rook, queen, king);
            result.Tint = piece.Side == Side.Black && black == null ? blackTint : Color.white;
            return result;
        }

        bool TrySkin(string id, Piece piece, ref Resolved result)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var entry in skins)
            {
                if (entry.skin != id || (entry.onlyForType && entry.type != piece.Type)) continue;
                bool hasBlack = piece.Side == Side.Black && entry.black != null && entry.black.Length > 0;
                var list = hasBlack ? entry.black : entry.white;
                bool hasModel = entry.models != null && entry.models.Length > 0;
                if ((list == null || list.Length == 0) && !hasModel) continue;
                if (list != null && list.Length > 0) result.Sprite = list[piece.Id % list.Length];
                if (hasModel) result.Model = entry.models[piece.Id % entry.models.Length];
                result.Tint = piece.Side == Side.Black && !hasBlack ? variantBlackTint : Color.white;
                result.StandUp = entry.standUp;
                return result.Sprite != null || result.Model != null;
            }
            return false;
        }

        static Sprite Pick(PieceType type, Sprite p, Sprite n, Sprite b, Sprite r, Sprite q, Sprite k)
        {
            switch (type)
            {
                case PieceType.Pawn: return p;
                case PieceType.Knight: return n;
                case PieceType.Bishop: return b;
                case PieceType.Rook: return r;
                case PieceType.Queen: return q;
                default: return k;
            }
        }
    }
}
