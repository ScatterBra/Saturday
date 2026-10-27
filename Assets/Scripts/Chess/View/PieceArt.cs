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
            [Tooltip("3D 模型（FBX 或预制体）。配了模型就在棋盘上用模型代替贴图（贴图仍用在被吃区等 2D 界面里），多个模型按棋子编号固定选一个")]
            public GameObject[] models = Array.Empty<GameObject>();
            [Tooltip("第三阶段才换上的 3D 模型（白方）。多个模型按棋子编号固定选一个")]
            public GameObject[] stage3Models = Array.Empty<GameObject>();
            [Tooltip("第三阶段黑方的 3D 模型；留空时用白方模型乘上 variantBlackTint")]
            public GameObject[] stage3BlackModels = Array.Empty<GameObject>();
        }

        /// <summary>选出来的贴图和它代表的东西，PieceView 据此决定还要不要显示占位标记。</summary>
        public struct Resolved
        {
            public Sprite Sprite;
            /// <summary>不为 null 时棋盘上用这个 3D 模型代替贴图。</summary>
            public GameObject Model;
            public Color Tint;
            /// <summary>贴图已经表现了 Skin，不用再画彩色圆环。</summary>
            public bool ShowsSkin;
            /// <summary>贴图已经表现了冻结外观（例如树），不用再画冻结方块。</summary>
            public bool ShowsFrozen;
            /// <summary>模型是立牌（第三阶段的棋子模型）：正面朝着相机摆，不随机转。</summary>
            public bool Standee;
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

        [Header("Paper sprites (pieces that were captured once)")]
        [Tooltip("被吃过的普通棋子（在被吃区里、复活以后）一直用纸片版的样子；换过外观的（消防栓、主教变体等）保持原来的外观")]
        public Sprite paperPawn;
        public Sprite paperKnight;
        public Sprite paperBishop;
        public Sprite paperRook;
        public Sprite paperQueen;
        public Sprite paperKing;
        [Tooltip("黑方纸片版；留空时用白方纸片版乘上 blackTint")]
        public Sprite blackPaperPawn;
        public Sprite blackPaperKnight;
        public Sprite blackPaperBishop;
        public Sprite blackPaperRook;
        public Sprite blackPaperQueen;
        public Sprite blackPaperKing;

        [Header("Stage 3 models (after the storm every piece becomes a 3D model)")]
        [Tooltip("第三阶段白方基础棋子的 3D 模型；某种棋子留空就继续用贴图")]
        public GameObject pawnModel;
        public GameObject knightModel;
        public GameObject bishopModel;
        public GameObject rookModel;
        public GameObject queenModel;
        public GameObject kingModel;
        [Tooltip("第三阶段黑方的 3D 模型；留空时用白方模型乘上 blackTint")]
        public GameObject blackPawnModel;
        public GameObject blackKnightModel;
        public GameObject blackBishopModel;
        public GameObject blackRookModel;
        public GameObject blackQueenModel;
        public GameObject blackKingModel;
        [Tooltip("第三阶段模型额外绕竖轴转多少度。0 = 模型正面朝着相机；模型背对相机时填 180")]
        public float stage3ModelYaw = 0f;

        [Tooltip("黑方没有单独贴图时，白方贴图乘上这个颜色")]
        public Color blackTint = new Color(0.42f, 0.42f, 0.48f);
        [Tooltip("外观贴图没有黑方版本时乘上这个颜色（彩色的图压得太暗会看不清，所以比 blackTint 浅）")]
        public Color variantBlackTint = new Color(0.75f, 0.75f, 0.8f);
        [Tooltip("棋子贴图在棋盘上的高度（一格 = 1）")]
        public float pieceHeight = 0.95f;
        [Tooltip("3D 模型在棋盘上的高度（一格 = 1），按模型包围盒自动缩放")]
        public float modelHeight = 1.1f;
        [Tooltip("3D 模型最宽不超过这么多（一格 = 1）")]
        public float modelWidth = 0.95f;

        [Header("Skins")]
        public List<SkinArt> skins = new List<SkinArt>();

        /// <summary>
        /// 按优先级选图：冻结外观（例如树）> 规则外观（例如消防栓）> 纸片版（被吃过的普通棋子）> 基础棋子。
        /// stage3 = true 时（第三阶段的棋盘）外观和基础棋子配了第三阶段模型的就用模型；纸片版仍然是贴图。
        /// </summary>
        public Resolved Resolve(Piece piece, bool stage3 = false)
        {
            var result = new Resolved();
            if (piece.IsFrozen && TrySkin(piece.FrozenSkin, piece, stage3, ref result))
            {
                result.ShowsFrozen = true;
                return result;
            }
            if (TrySkin(piece.Skin, piece, stage3, ref result))
            {
                result.ShowsSkin = true;
                return result;
            }
            // 被吃过的普通棋子：被吃区里、复活以后都一直是纸片版（换过外观的保持原来的外观）
            if (piece.WasLost && string.IsNullOrEmpty(piece.Skin))
            {
                var blackPaper = piece.Side == Side.Black
                    ? Pick(piece.Type, blackPaperPawn, blackPaperKnight, blackPaperBishop, blackPaperRook, blackPaperQueen, blackPaperKing)
                    : null;
                var paper = blackPaper != null ? blackPaper : Pick(piece.Type, paperPawn, paperKnight, paperBishop, paperRook, paperQueen, paperKing);
                if (paper != null)
                {
                    result.Sprite = paper;
                    result.Tint = piece.Side == Side.Black && blackPaper == null ? blackTint : Color.white;
                    result.ShowsSkin = true;
                    return result;
                }
            }

            var black = piece.Side == Side.Black
                ? Pick(piece.Type, blackPawn, blackKnight, blackBishop, blackRook, blackQueen, blackKing)
                : null;
            result.Sprite = black != null ? black : Pick(piece.Type, pawn, knight, bishop, rook, queen, king);
            result.Tint = piece.Side == Side.Black && black == null ? blackTint : Color.white;
            if (stage3)
            {
                var blackModel = piece.Side == Side.Black
                    ? Pick(piece.Type, blackPawnModel, blackKnightModel, blackBishopModel, blackRookModel, blackQueenModel, blackKingModel)
                    : null;
                var model = blackModel != null ? blackModel : Pick(piece.Type, pawnModel, knightModel, bishopModel, rookModel, queenModel, kingModel);
                if (model != null)
                {
                    result.Model = model;
                    result.Standee = true;
                    result.Tint = piece.Side == Side.Black && blackModel == null ? blackTint : Color.white;
                }
            }
            return result;
        }

        bool TrySkin(string id, Piece piece, bool stage3, ref Resolved result)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var entry in skins)
            {
                if (entry.skin != id || (entry.onlyForType && entry.type != piece.Type)) continue;
                bool hasBlack = piece.Side == Side.Black && entry.black != null && entry.black.Length > 0;
                var list = hasBlack ? entry.black : entry.white;
                bool hasModel = entry.models != null && entry.models.Length > 0;
                bool hasBlackStage3 = piece.Side == Side.Black && entry.stage3BlackModels != null && entry.stage3BlackModels.Length > 0;
                var stage3List = hasBlackStage3 ? entry.stage3BlackModels : entry.stage3Models;
                bool hasStage3 = stage3 && stage3List != null && stage3List.Length > 0;
                if ((list == null || list.Length == 0) && !hasModel && !hasStage3) continue;
                if (list != null && list.Length > 0) result.Sprite = list[piece.Id % list.Length];
                if (hasModel) result.Model = entry.models[piece.Id % entry.models.Length];
                result.Tint = piece.Side == Side.Black && !hasBlack ? variantBlackTint : Color.white;
                if (hasStage3)
                {
                    result.Model = stage3List[piece.Id % stage3List.Length];
                    result.Standee = true;
                    result.Tint = piece.Side == Side.Black && !hasBlackStage3 ? variantBlackTint : Color.white;
                }
                return result.Sprite != null || result.Model != null;
            }
            return false;
        }

        static T Pick<T>(PieceType type, T p, T n, T b, T r, T q, T k)
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
