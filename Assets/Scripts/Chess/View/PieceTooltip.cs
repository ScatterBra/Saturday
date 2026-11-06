namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 鼠标停在棋子上时弹出的说明：名字、属于哪一方、有什么功能、现在的状态。
    /// 只给“有特殊外观或状态”的棋子（变了外观、被冻住、复活回来的），普通棋子不弹。
    /// 游戏里看到的文字都在这里，要改措辞改这个文件就行。
    /// </summary>
    public static class PieceTooltip
    {
        public struct Info
        {
            /// <summary>名字，例如 Fire Hydrant。</summary>
            public string Title;
            /// <summary>Yours / Your friend's。</summary>
            public string Owner;
            /// <summary>能做什么（可以为空）。</summary>
            public string Body;
            /// <summary>现在的状态，例如还要冻几回合（可以为空）。</summary>
            public string Status;
            public bool IsYours;
        }

        /// <summary>这枚棋子要不要弹说明。</summary>
        public static bool HasTooltip(Piece piece) =>
            piece != null && piece.OnBoard && (!string.IsNullOrEmpty(piece.Skin) || piece.IsFrozen || piece.WasLost);

        public static Info? Describe(Piece piece, Side playerSide, bool stage3)
        {
            if (!HasTooltip(piece)) return null;
            bool yours = piece.Side == playerSide;
            string type = TypeName(piece.Type);
            string moves = $"Moves like a {type.ToLowerInvariant()}.";
            var info = new Info { IsYours = yours, Owner = yours ? "Yours" : "Your friend's", Title = type };

            switch (piece.Skin)
            {
                case FireHydrantRule.Hydrant:
                    info.Title = "Fire Hydrant";
                    info.Body = $"{moves} Every time it lands, it sprays an enemy right next to it up to {HydrantSpray.MaxDistance} squares away.";
                    if (stage3) info.Body += " Spray one into lava and it's gone.";
                    break;
                case FireHydrantRule.Frog:
                    info.Title = "Frog";
                    info.Body = $"{moves} Every time it lands, it pulls the closest enemy in a straight line (2-{FrogTongue.MaxReach} squares away) one step closer.";
                    if (stage3) info.Body += " Pull one into lava and it's gone.";
                    break;
                case "elephant":
                case "fish_shop":
                case "bishbird":
                case "cake":
                    info.Title = SkinName(piece.Skin);
                    info.Body = $"A bishop in disguise. {moves}";
                    break;
                case "dragon":
                    info.Title = "Dragon";
                    info.Body = $"Already breathed its fire. {moves}";
                    break;
                case "crayon":
                    info.Title = $"Crayon {type}";
                    // TODO 蜡笔棋子之后会有特殊功能，现在先占位
                    info.Body = $"Came back from the lost pile, drawn in crayon. {moves} (Crayon power: coming soon.)";
                    break;
                case null:
                case "":
                    if (piece.WasLost)
                    {
                        info.Title = $"Paper {type}";
                        info.Body = $"Came back from the lost pile. {moves}";
                    }
                    break;
                default:
                    info.Title = SkinName(piece.Skin);
                    info.Body = moves;
                    break;
            }

            if (piece.IsFrozen)
            {
                string turns = piece.FrozenTurns == 1 ? "1 more turn" : $"{piece.FrozenTurns} more turns";
                switch (piece.FrozenSkin)
                {
                    case "tree":
                        // 变成树的兵：名字就叫树
                        if (string.IsNullOrEmpty(piece.Skin))
                        {
                            info.Title = "Tree";
                            info.Body = "A pawn that grew roots.";
                        }
                        info.Status = $"Rooted. Can't move for {turns}.";
                        break;
                    case "poop":
                        info.Status = $"Covered in bird mess. Can't move for {turns}.";
                        break;
                    default:
                        info.Status = $"Can't move for {turns}.";
                        break;
                }
            }
            return info;
        }

        public static string TypeName(PieceType type) => type.ToString();

        static string SkinName(string skin)
        {
            switch (skin)
            {
                case "elephant": return "Elephant";
                case "fish_shop": return "Fish Shop";
                case "bishbird": return "Bish-bird";
                case "cake": return "Cake";
                default: return char.ToUpperInvariant(skin[0]) + skin.Substring(1).Replace('_', ' ');
            }
        }
    }
}
