using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 第三阶段（城堡营救）的地形。用一张 8 × 8 的字符网格描述，第一行是第 8 行，最后一行是第 1 行，
    /// 每行从 a 列到 h 列，空格会被忽略。
    ///
    ///   .  平地（旧格式里的 _ 也当平地）
    ///   1-9 升起的高原，数字是高度（只是画面效果，不影响走法；画面上每级 0.5 格）
    ///   C  城堡（高度固定为 CastleHeight），只有玩家的马能进，马进去就赢
    ///   ~  固定的岩浆格（一般不用：风暴时会另外随机撒 GameSettings.LavaSquares 格岩浆）
    ///
    /// 棋子不在这里摆：风暴把双方的棋子吹散，随机落到棋盘上（Board.Scatter）。
    /// </summary>
    public sealed class Stage3Layout
    {
        public const int CastleHeight = 5;

        public static readonly string DefaultTerrain =
            "CC43....\n" +
            "C432....\n" +
            "3321....\n" +
            "21......\n" +
            "........\n" +
            "........\n" +
            "........\n" +
            "........";

        public readonly int[] Heights = new int[64];
        public readonly bool[] Castle = new bool[64];
        public readonly bool[] Lava = new bool[64];

        public static Stage3Layout Default => Parse(DefaultTerrain);

        public static Stage3Layout Parse(string terrain)
        {
            var layout = new Stage3Layout();
            var t = Rows(terrain, nameof(terrain));
            for (int row = 0; row < 8; row++)
            {
                int y = 7 - row;
                for (int x = 0; x < 8; x++)
                {
                    var c = new Coord(x, y);
                    char tc = t[row][x];
                    switch (tc)
                    {
                        case '.': case '_': break;
                        case 'C': case 'c': layout.Castle[c.Index] = true; layout.Heights[c.Index] = CastleHeight; break;
                        case '~': layout.Lava[c.Index] = true; break;
                        default:
                            if (tc >= '1' && tc <= '9') layout.Heights[c.Index] = tc - '0';
                            else throw new FormatException($"Unknown terrain character '{tc}' at {c}");
                            break;
                    }
                }
            }
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
    }
}
