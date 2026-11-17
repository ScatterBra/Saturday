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

        /// <summary>这枚棋子要不要弹说明（第三阶段潮湿的、城堡里的、玩家的象和马也弹）。</summary>
        public static bool HasTooltip(Piece piece, Board board = null)
        {
            if (piece == null || !piece.OnBoard) return false;
            if (!string.IsNullOrEmpty(piece.Skin) || piece.IsFrozen || piece.WasLost || piece.IsWet) return true;
            if (board == null || !board.Stage3) return false;
            if (board.InCastle(piece)) return true;
            return piece.Side == board.Rescuer && (piece.Type == PieceType.Bishop || piece.Type == PieceType.Knight);
        }

        public static Info? Describe(Piece piece, Side playerSide, bool stage3, Board board = null)
        {
            if (!HasTooltip(piece, board)) return null;
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
                case "bishbird":
                    info.Title = SkinName(piece.Skin);
                    info.Body = "Completely useless. All it does is squawk.";
                    break;
                case ChessGame.MarieSkin:
                case ChessGame.CleopatraSkin:
                    info.Title = SkinName(piece.Skin);
                    info.Body = $"A queen in costume. {moves}";
                    break;
                case "fish_shop":
                    info.Title = SkinName(piece.Skin);
                    info.Body = $"A bishop in disguise. {moves}";
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

            if (board != null && board.InCastle(piece))
            {
                info.Title = piece.Type == PieceType.King ? "The King" : piece.Type == PieceType.Queen ? "The Queen" : type;
                info.Body = board.GateOpen
                    ? "Hiding in the castle. The gate is open: walk any piece in to take over."
                    : piece.Type == PieceType.Queen
                        ? "Hiding in the castle. Casts spells now and then; the marked squares get hit next turn."
                        : "Hiding in the castle. Take out every guard to open the gate.";
            }
            else if (stage3 && yours && (piece.Type == PieceType.Bishop || piece.Type == PieceType.Knight))
            {
                string skill = piece.Type == PieceType.Bishop
                    ? "Storm power: fires a beam along a diagonal (hits your own pieces too)."
                    : $"Storm power: turns into a dragon and burns an enemy pawn, knight or bishop up to {DragonKnightRule.Reach} squares away.";
                info.Body = string.IsNullOrEmpty(info.Body) ? $"{moves} {skill}" : $"{info.Body} {skill}";
            }

            if (piece.IsWet)
            {
                string wet = piece.WetTurns == 1 ? "1 more turn" : $"{piece.WetTurns} more turns";
                info.Status = $"Soaked for {wet}. Fire makes it explode.";
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
                        info.Status = Join(info.Status, $"Rooted. Can't move for {turns}.");
                        break;
                    case IceLanceRule.Ice:
                        info.Status = Join(info.Status, $"Frozen solid. Can't move for {turns}.");
                        break;
                    default:
                        info.Status = Join(info.Status, $"Can't move for {turns}.");
                        break;
                }
            }
            return info;
        }

        public static string TypeName(PieceType type) => type.ToString();

        static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "\n" + b;

        static string SkinName(string skin)
        {
            switch (skin)
            {
                case "elephant": return "Elephant";
                case "fish_shop": return "Fish Shop";
                case "bishbird": return "Bish-bird";
                case "cake": return "Cake";
                case ChessGame.MarieSkin: return "Marie Antoinette";
                case ChessGame.CleopatraSkin: return "Cleopatra";
                default: return char.ToUpperInvariant(skin[0]) + skin.Substring(1).Replace('_', ' ');
            }
        }
    }
}
