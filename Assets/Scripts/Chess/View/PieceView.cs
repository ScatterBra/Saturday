using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 一枚棋子的占位显示：圆柱底座（阵营颜色）+ 字母，外观（Skin）用底下的彩色圆环和小字表示，
    /// 冻结用头顶的小方块表示，城堡换成方块。PieceArt 里配了贴图的棋子类型改用朝向镜头的贴图显示。
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
        readonly GameObject tower;
        readonly GameObject ring;
        readonly GameObject frozenMarker;
        readonly TextMesh letter;
        readonly TextMesh caption;
        readonly SpriteRenderer sprite;

        public PieceView(ChessViewFactory factory, Transform parent, Piece piece)
        {
            this.factory = factory;
            PieceId = piece.Id;
            Root = new GameObject($"Piece {piece.Id} {piece.Side} {piece.Type}");
            Root.transform.SetParent(parent, false);

            disc = factory.Primitive(PrimitiveType.Cylinder, Root.transform, new Vector3(0, 0.1f, 0), new Vector3(0.72f, 0.1f, 0.72f));
            tower = factory.Primitive(PrimitiveType.Cube, Root.transform, new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f));
            ring = factory.Primitive(PrimitiveType.Cylinder, Root.transform, new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.02f, 0.9f));
            frozenMarker = factory.Primitive(PrimitiveType.Cube, Root.transform, new Vector3(0.28f, 0.45f, 0.28f), new Vector3(0.22f, 0.22f, 0.22f));

            letter = factory.Label(Root.transform, new Vector3(0, 0.32f, 0), 0.06f, "");
            caption = factory.Label(Root.transform, new Vector3(0, 0.3f, -0.46f), 0.036f, "");
            caption.fontStyle = FontStyle.Bold;
            if (factory.Art != null) sprite = factory.Billboard(Root.transform, new Vector3(0, 0.01f, 0));

            Refresh(piece);
        }

        public void Refresh(Piece piece)
        {
            bool white = piece.Side == Side.White;
            var art = factory.Art;
            var resolved = art != null ? art.Resolve(piece) : default;
            bool useSprite = resolved.Sprite != null;
            if (sprite != null)
            {
                sprite.enabled = useSprite;
                sprite.sprite = resolved.Sprite;
                sprite.color = resolved.Tint;
                // 城堡在 3D 模型做好之前先用放大的车表示
                sprite.transform.localScale = Vector3.one * art.pieceHeight * (piece.IsCastle ? 1.2f : 1f);
            }
            letter.gameObject.SetActive(!useSprite);

            disc.SetActive(!useSprite && !piece.IsCastle);
            tower.SetActive(!useSprite && piece.IsCastle);
            factory.SetColor(piece.IsCastle ? tower : disc, white ? WhiteBody : BlackBody);

            // 贴图已经画出来的外观就不再显示占位圆环 / 冻结方块和对应的小字
            string skin = resolved.ShowsSkin ? null : piece.Skin;
            ring.SetActive(skin != null);
            if (skin != null) factory.SetColor(ring, SkinColor(skin));

            frozenMarker.SetActive(piece.IsFrozen && !resolved.ShowsFrozen);
            if (piece.IsFrozen) factory.SetColor(frozenMarker, SkinColor(piece.FrozenSkin));

            letter.text = PieceValues.Letter(piece.Type);
            letter.color = piece.IsCastle ? WhiteInk : (white ? WhiteInk : BlackInk);
            letter.transform.localPosition = new Vector3(0, piece.IsCastle ? 0.72f : 0.32f, 0);

            string text = skin ?? "";
            if (piece.IsFrozen)
            {
                // 冻结外观有图时只显示剩余回合数
                string frozen = resolved.ShowsFrozen ? piece.FrozenTurns.ToString() : $"{piece.FrozenSkin} {piece.FrozenTurns}";
                text = frozen + (text.Length > 0 ? "\n" + text : "");
            }
            if (piece.HasGuest) text += "\n+guest";
            caption.text = text;
            caption.color = new Color(0.1f, 0.06f, 0.04f);
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
                case "castle": return new Color(0.6f, 0.6f, 0.62f);
                case "tree": return new Color(0.15f, 0.55f, 0.2f);
                case "poop": return new Color(0.45f, 0.3f, 0.15f);
                default: return Color.magenta;
            }
        }
    }
}
