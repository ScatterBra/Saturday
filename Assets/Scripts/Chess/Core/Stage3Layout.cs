using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 第三阶段（城堡营救）的地形和阵型。用两张 8 × 8 的字符网格描述，第一行是第 8 行，最后一行是第 1 行，
    /// 每行从 a 列到 h 列，空格会被忽略。
    ///
    /// 地形：
    ///   _  我方一侧的平地（献祭完成前我方棋子只能在这些格子活动，或者走进岩浆献祭）
    ///   .  对面的平地
    ///   1-9 升起的高原，数字是高度（只是画面效果，不影响走法；画面上每级 0.5 格）
    ///   C  城堡（高度固定为 CastleHeight），只有马能进（要先完成献祭），马进去就赢
    ///   ~  岩浆（献祭完成前走进去 = 献祭；完成后可以站。被弹进去：普通棋子死，不死的棋子停一回合）
    ///
    /// 阵型：
    ///   p n b r q k  朋友（对手）的棋子
    ///   K  我方的王
    ///   N  我方马的位置
    ///   *  我方其他棋子的位置（按第 1 行到第 8 行、a 到 h 的顺序填）
    ///   .  空
    /// 我方棋子保留风暴前的种类和数量，按上面的位置重新摆；位置不够时放到我方一侧任意空格，还不够就放到离我方王最近的空平地。
    /// </summary>
    public sealed class Stage3Layout
    {
        public const int CastleHeight = 5;

        public static readonly string DefaultTerrain =
            "CC43....\n" +
            "C432....\n" +
            "3321....\n" +
            "21......\n" +
            "......~~\n" +
            "..~~~~~_\n" +
            ".~~_____\n" +
            "~~______";

        public static readonly string DefaultPieces =
            "..rk....\n" +
            "..bq....\n" +
            "..nn....\n" +
            "pppp....\n" +
            "....pp..\n" +
            ".......*\n" +
            "...*N**N\n" +
            "..***K**";

        public readonly int[] Heights = new int[64];
        public readonly bool[] Castle = new bool[64];
        public readonly bool[] Lava = new bool[64];
        public readonly bool[] PlayerZone = new bool[64];
        public readonly List<(PieceType Type, Coord At)> FriendPieces = new List<(PieceType, Coord)>();
        public Coord? PlayerKing;
        public readonly List<Coord> KnightSlots = new List<Coord>();
        public readonly List<Coord> OtherSlots = new List<Coord>();

        public static Stage3Layout Default => Parse(DefaultTerrain, DefaultPieces);

        public static Stage3Layout Parse(string terrain, string pieces)
        {
            var layout = new Stage3Layout();
            var t = Rows(terrain, nameof(terrain));
            var p = Rows(pieces, nameof(pieces));
            var slots = new List<(Coord at, char c)>();
            for (int row = 0; row < 8; row++)
            {
                int y = 7 - row;
                for (int x = 0; x < 8; x++)
                {
                    var c = new Coord(x, y);
                    char tc = t[row][x];
                    switch (tc)
                    {
                        case '_': layout.PlayerZone[c.Index] = true; break;
                        case '.': break;
                        case 'C': case 'c': layout.Castle[c.Index] = true; layout.Heights[c.Index] = CastleHeight; break;
                        case '~': layout.Lava[c.Index] = true; break;
                        default:
                            if (tc >= '1' && tc <= '9') layout.Heights[c.Index] = tc - '0';
                            else throw new FormatException($"Unknown terrain character '{tc}' at {c}");
                            break;
                    }

                    char pc = p[row][x];
                    if (pc == '.') continue;
                    if (layout.Castle[c.Index] || layout.Lava[c.Index])
                        throw new FormatException($"Piece '{pc}' at {c} sits on the castle or lava");
                    if (pc == 'K') layout.PlayerKing = c;
                    else if (pc == 'N' || pc == '*') slots.Add((c, pc));
                    else if (TryType(pc, out var type)) layout.FriendPieces.Add((type, c));
                    else throw new FormatException($"Unknown piece character '{pc}' at {c}");
                }
            }
            // 我方位置按第 1 行到第 8 行、a 到 h 的顺序
            slots.Sort((a, b) => a.at.Y != b.at.Y ? a.at.Y.CompareTo(b.at.Y) : a.at.X.CompareTo(b.at.X));
            foreach (var (at, c) in slots) (c == 'N' ? layout.KnightSlots : layout.OtherSlots).Add(at);
            return layout;
        }

        static string[] Rows(string grid, string name)
        {
            var rows = new List<string>();
            foreach (var line in (grid ?? "").Split('\n'))
            {
                var row = line.Replace(" ", "").Replace("\t", "").Replace("\r", "");
                if (row.Length > 0) rows.Add(row);
            }
            if (rows.Count != 8) throw new FormatException($"{name} needs 8 rows, got {rows.Count}");
            foreach (var row in rows)
                if (row.Length != 8) throw new FormatException($"{name} row '{row}' needs 8 characters");
            return rows.ToArray();
        }

        static bool TryType(char c, out PieceType type)
        {
            switch (c)
            {
                case 'p': type = PieceType.Pawn; return true;
                case 'n': type = PieceType.Knight; return true;
                case 'b': type = PieceType.Bishop; return true;
                case 'r': type = PieceType.Rook; return true;
                case 'q': type = PieceType.Queen; return true;
                case 'k': type = PieceType.King; return true;
                default: type = PieceType.Pawn; return false;
            }
        }
    }
}
