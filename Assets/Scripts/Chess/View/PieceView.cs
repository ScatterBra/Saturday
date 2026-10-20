using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 一枚棋子的占位显示：圆柱底座（阵营颜色）+ 字母，外观（Skin）用底下的彩色圆环和小字表示，
    /// 冻结用头顶的小方块表示。PieceArt 里配了贴图的棋子类型改用朝向镜头的贴图显示，配了 3D 模型的外观（例如树）用模型显示。
    /// </summary>
    public sealed class PieceView
    {
        static readonly Color WhiteBody = new Color(0.96f, 0.93f, 0.86f);
        static readonly Color BlackBody = new Color(0.2f, 0.2f, 0.25f);
        static readonly Color WhiteInk = new Color(0.15f, 0.12f, 0.1f);
        static readonly Color BlackInk = new Color(0.95f, 0.93f, 0.88f);

        public readonly int PieceId;
        public readonly GameObject Root;

        readonly ChessViewFactory factory;
        readonly GameObject disc;
        readonly GameObject ring;
        readonly GameObject frozenMarker;
        readonly TextMesh letter;
        readonly TextMesh caption;
        readonly SpriteRenderer sprite;
        readonly FaceCamera spriteFacing;
        GameObject model;
        GameObject modelSource;

        // 调整棋子在棋盘上的位置：平躺的贴图枢轴在底边，往近处挪半格让图居中；立起来的贴图站在格子中间
        static readonly Vector3 FlatOffset = new Vector3(0, 0.01f, -0.45f);
        static readonly Vector3 StandOffset = new Vector3(0, 0.01f, 0f);

        public PieceView(ChessViewFactory factory, Transform parent, Piece piece)
        {
            this.factory = factory;
            PieceId = piece.Id;
            Root = new GameObject($"Piece {piece.Id} {piece.Side} {piece.Type}");
            Root.transform.SetParent(parent, false);

            disc = factory.Primitive(PrimitiveType.Cylinder, Root.transform, new Vector3(0, 0.1f, 0), new Vector3(0.72f, 0.1f, 0.72f));
            ring = factory.Primitive(PrimitiveType.Cylinder, Root.transform, new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.02f, 0.9f));
            frozenMarker = factory.Primitive(PrimitiveType.Cube, Root.transform, new Vector3(0.28f, 0.45f, 0.28f), new Vector3(0.22f, 0.22f, 0.22f));

            letter = factory.Label(Root.transform, new Vector3(0, 0.32f, 0), 0.06f, "");
            caption = factory.Label(Root.transform, new Vector3(0, 0.3f, -0.46f), 0.036f, "");
            caption.fontStyle = FontStyle.Bold;
            if (factory.Art != null)
            {
                sprite = factory.Billboard(Root.transform, FlatOffset);
                spriteFacing = sprite.GetComponent<FaceCamera>();
            }

            Refresh(piece);
        }

        public void Refresh(Piece piece)
        {
            bool white = piece.Side == Side.White;
            var art = factory.Art;
            var resolved = art != null ? art.Resolve(piece) : default;
            UpdateModel(resolved.Model, piece);
            bool useModel = model != null;
            bool useSprite = !useModel && resolved.Sprite != null;
            if (sprite != null)
            {
                sprite.enabled = useSprite;
                sprite.sprite = resolved.Sprite;
                sprite.color = resolved.Tint;
                sprite.transform.localScale = Vector3.one * art.pieceHeight;
                // 立起来的贴图（例如树）站在格子中间，和棋盘垂直；其余贴图平躺在棋盘上
                bool stand = useSprite && resolved.StandUp;
                sprite.transform.localPosition = stand ? StandOffset : FlatOffset;
                if (spriteFacing != null)
                {
                    spriteFacing.standUp = stand;
                    spriteFacing.standAngle = art.standAngle;
                }
            }
            letter.gameObject.SetActive(!useSprite && !useModel);

            disc.SetActive(!useSprite && !useModel);
            factory.SetColor(disc, white ? WhiteBody : BlackBody);

            // 贴图已经画出来的外观就不再显示占位圆环 / 冻结方块和对应的小字
            // 用贴图显示时不画圆环（俯视角下圆环会盖住贴图），改成给贴图染上外观的颜色
            string skin = resolved.ShowsSkin ? null : piece.Skin;
            ring.SetActive(skin != null && !useSprite);
            if (skin != null && !useSprite) factory.SetColor(ring, SkinColor(skin));
            if (skin != null && useSprite && sprite != null)
                sprite.color = resolved.Tint * Color.Lerp(Color.white, SkinColor(skin), 0.6f);

            frozenMarker.SetActive(piece.IsFrozen && !resolved.ShowsFrozen);
            if (piece.IsFrozen) factory.SetColor(frozenMarker, SkinColor(piece.FrozenSkin));

            letter.text = PieceValues.Letter(piece.Type);
            letter.color = white ? WhiteInk : BlackInk;

            string text = skin ?? "";
            if (piece.IsFrozen)
            {
                // 冻结外观有图时只显示剩余回合数
                string frozen = resolved.ShowsFrozen ? piece.FrozenTurns.ToString() : $"{piece.FrozenSkin} {piece.FrozenTurns}";
                text = frozen + (text.Length > 0 ? "\n" + text : "");
            }
            caption.text = text;
            caption.color = new Color(0.1f, 0.06f, 0.04f);
        }

        /// <summary>外观要用的模型变了才重新生成；同一枚棋子总是同一个朝向，几棵树看起来不会一模一样。</summary>
        void UpdateModel(GameObject source, Piece piece)
        {
            if (source == modelSource) return;
            if (model != null) Object.Destroy(model);
            modelSource = source;
            model = source != null
                ? factory.Model(source, Root.transform, factory.Art.modelHeight, factory.Art.modelWidth, piece.Id * 137f)
                : null;
        }

        public void SetPosition(Vector3 localPosition) => Root.transform.localPosition = localPosition;

        public void Destroy() => Object.Destroy(Root);

        /// <summary>外观对应的占位颜色。</summary>
        public static Color SkinColor(string skin)
        {
            switch (skin)
            {
                case "hydrant": return new Color(0.9f, 0.15f, 0.15f);
                case "elephant": return new Color(0.55f, 0.62f, 0.75f);
                case "bish_hop": return new Color(0.6f, 0.3f, 0.85f);
                case "fish_shop": return new Color(0.2f, 0.6f, 0.9f);
                case "bishbug": return new Color(0.4f, 0.75f, 0.2f);
                case "bishbird": return new Color(0.95f, 0.75f, 0.2f);
                case "bishblob": return new Color(0.3f, 0.9f, 0.6f);
                case "dragon": return new Color(1f, 0.45f, 0.05f);
                case "crayon": return new Color(1f, 0.85f, 0.2f);
                case "hellhorse": return new Color(0.55f, 0.05f, 0.05f);
                case "tree": return new Color(0.15f, 0.55f, 0.2f);
                case "poop": return new Color(0.45f, 0.3f, 0.15f);
                default: return Color.magenta;
            }
        }
    }
}
