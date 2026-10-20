using System;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class Stage3Tests
    {
        static Coord C(string square) => new Coord(square[0] - 'a', square[1] - '1');

        const string Flat =
            "........\n........\n........\n........\n........\n........\n........\n........";

        /// <summary>造一个只有指定我方棋子的棋盘，再铺上第三阶段的地形和阵型。</summary>
        static Board Setup(string terrain, string pieces, int sacrificesNeeded, params PieceType[] mine)
        {
            var board = new Board();
            for (int i = 0; i < mine.Length; i++) board.AddPiece(mine[i], Side.White, Coord.FromIndex(i));
            board.ApplyLayout(Stage3Layout.Parse(terrain, pieces), Side.White, sacrificesNeeded);
            return board;
        }

        static Coord[] Targets(Board board, string square) =>
            MoveGenerator.MovesFor(board, board.PieceAt(C(square))).Select(m => m.To).ToArray();

        static MoveResult Play(Board board, string from, string to)
        {
            var result = board.Apply(new Move(board.PieceAt(C(from)).Id, C(from), C(to)));
            if (board.SideToMove == Side.Black) board.Pass(); // 测试里只走我方，跳过对手
            return result;
        }

        [Test]
        public void DefaultLayout_Parses()
        {
            var layout = Stage3Layout.Default;
            foreach (var s in new[] { "a7", "a8", "b8" }) Assert.IsTrue(layout.Castle[C(s).Index], s);
            Assert.AreEqual(3, layout.Castle.Count(c => c));
            Assert.AreEqual(Stage3Layout.CastleHeight, layout.Heights[C("a8").Index]);
            Assert.AreEqual(2, layout.Heights[C("b7").Index]);
            Assert.AreEqual(7, layout.Lava.Count(l => l));
            Assert.IsTrue(layout.PlayerZone[C("h3").Index], "gap in the river");
            Assert.AreEqual(17, layout.PlayerZone.Count(z => z));
            Assert.AreEqual(12, layout.FriendPieces.Count);
            Assert.AreEqual(C("e1"), layout.PlayerKing);
            CollectionAssert.AreEqual(new[] { C("b2"), C("g2") }, layout.KnightSlots);
        }

        [Test]
        public void Parse_RejectsBadGrids()
        {
            Assert.Throws<FormatException>(() => Stage3Layout.Parse("........", Flat));
            Assert.Throws<FormatException>(() => Stage3Layout.Parse(Flat.Replace("........\n........\n", "....x...\n........\n"), Flat));
            Assert.Throws<FormatException>(() => Stage3Layout.Parse("C.......\n" + Flat.Substring(9), "p.......\n" + Flat.Substring(9)));
        }

        [Test]
        public void ApplyLayout_KeepsPlayerPiecesAndEffects_RetiresFriendPieces()
        {
            var board = Board.CreateStandard();
            var lostKnight = board.PieceAt(C("b1"));
            board.RemovePiece(lostKnight);
            var rook = board.PieceAt(C("h1"));
            rook.Skin = "hydrant";
            var pawn = board.PieceAt(C("a2"));
            pawn.FrozenTurns = 2;
            pawn.FrozenSkin = "tree";

            board.ApplyLayout(Stage3Layout.Default, Side.White, 3);

            Assert.IsTrue(board.Stage3);
            Assert.AreEqual(Side.White, board.SideToMove);
            Assert.AreEqual(C("e1"), board.King(Side.White).Pos);
            Assert.AreEqual(C("b2"), board.PieceAt(C("b2")).Pos);
            Assert.AreEqual(PieceType.Knight, board.PieceAt(C("b2")).Type, "the remaining knight goes to the first knight slot");
            Assert.AreEqual("hydrant", rook.Skin);
            Assert.IsTrue(rook.OnBoard);
            Assert.AreEqual(2, pawn.FrozenTurns, "effects carry over");
            Assert.AreEqual(15, board.PiecesOf(Side.White).Count());
            CollectionAssert.Contains(board.CapturedOf(Side.White).ToList(), lostKnight);
            Assert.AreEqual(12, board.PiecesOf(Side.Black).Count());
            Assert.AreEqual(0, board.CapturedOf(Side.Black).Count(), "pieces swept away by the storm are not 'captured'");
            Assert.AreEqual(C("d8"), board.King(Side.Black).Pos);
        }

        const string RiverTerrain =
            "........\n........\n........\n........\n........\n~~~~~~~_\n________\n________";

        [Test]
        public void BeforeTheTask_PlayerStaysOnItsSide_AndCanSacrificeIntoLava()
        {
            const string pieces = "........\n........\n........\n........\n........\n........\n*...K...\n.......*";
            var board = Setup(RiverTerrain, pieces, 3, PieceType.King, PieceType.Rook, PieceType.Pawn);
            // 我方位置顺序：h1、a2 → 车在 h1，兵在 a2，王在 e2
            CollectionAssert.AreEquivalent(new[] { C("h2"), C("h3"), C("g1"), C("f1"), C("e1"), C("d1"), C("c1"), C("b1"), C("a1") }, Targets(board, "h1"),
                "h4 is across the river, so the rook stops at the gap");
            CollectionAssert.AreEquivalent(new[] { C("a3") }, Targets(board, "a2"), "the pawn may walk into the lava (sacrifice)");
            CollectionAssert.DoesNotContain(Targets(board, "e2"), C("e3"), "the king can never be sacrificed");

            var result = Play(board, "a2", "a3");
            Assert.IsTrue(result.Sacrificed);
            Assert.AreEqual(1, board.Sacrifices);
            Assert.IsFalse(board.TaskComplete);
            Assert.IsNull(board.PieceAt(C("a3")));
            Assert.AreEqual(0, board.CapturedOf(Side.White).Count(), "sacrificed pieces never come back");
        }

        [Test]
        public void SacrificingEnough_TurnsKnightsIntoHellhorses_ThatWalkOnLava()
        {
            const string pieces = "........\n........\n........\n........\n........\n........\n*N**....\n....K...";
            var board = Setup(RiverTerrain, pieces, 3,
                PieceType.King, PieceType.Knight, PieceType.Pawn, PieceType.Pawn, PieceType.Pawn);
            Play(board, "a2", "a3");
            Play(board, "c2", "c3");
            Assert.IsFalse(board.PieceAt(C("b2")).IsHellhorse);
            var last = Play(board, "d2", "d3");
            Assert.IsTrue(last.TaskCompleted);
            Assert.IsTrue(board.TaskComplete);

            var horse = board.PieceAt(C("b2"));
            Assert.IsTrue(horse.IsHellhorse);
            CollectionAssert.AreEquivalent(new[] { C("b3"), C("a2"), C("c2"), C("b1") }, Targets(board, "b2"),
                "one step forward/back/left/right, lava included");

            Play(board, "b2", "b3");
            Assert.AreSame(horse, board.PieceAt(C("b3")), "a hellhorse stands on lava");
            CollectionAssert.Contains(Targets(board, "b3"), C("b4"), "after the task the far side is open");
        }

        const string PlateauTerrain =
            "C2......\n21......\n1.1.....\n........\n........\n........\n........\n........";

        [Test]
        public void Plateau_ClimbOneLevelAtATime_AndOnlyKnightsEnterTheCastle()
        {
            const string pieces = ".*......\n........\n........\nN.......\n........\n........\n........\n..*.K...";
            var board = Setup(PlateauTerrain, pieces, 0, PieceType.King, PieceType.Knight, PieceType.Rook, PieceType.Rook);
            // 我方位置顺序：c1、b8 → 两个车在 c1 和 b8，马在 a5

            var knightMoves = Targets(board, "a5");
            CollectionAssert.Contains(knightMoves, C("a6"), "climb one level by stepping");
            CollectionAssert.Contains(knightMoves, C("c4"));
            CollectionAssert.DoesNotContain(knightMoves, C("b7"), "no jumping onto the plateau");
            CollectionAssert.DoesNotContain(knightMoves, C("c6"), "c6 is raised too");

            Play(board, "a5", "a6");
            CollectionAssert.AreEquivalent(new[] { C("a7"), C("a5"), C("b6") }, Targets(board, "a6"),
                "on the plateau: orthogonal single steps only");

            var rookTargets = Targets(board, "b8");
            CollectionAssert.DoesNotContain(rookTargets, C("a8"), "only knights may enter the castle");
            CollectionAssert.DoesNotContain(rookTargets, C("c8"), "a two-level drop is too steep");
            CollectionAssert.Contains(rookTargets, C("b7"));

            var low = Targets(board, "c1");
            CollectionAssert.Contains(low, C("c5"));
            CollectionAssert.DoesNotContain(low, C("c6"), "the plateau blocks sliding");

            Play(board, "a6", "a7");
            var result = Play(board, "a7", "a8");
            Assert.IsTrue(result.Rescued);
        }

        [Test]
        public void FriendKnocksPiecesBack_PreferringLava_AndNeverAttacksTheKing()
        {
            const string terrain = "........\n........\n........\n........\n........\n........\n...~....\n........";
            const string pieces = "r.......\n........\n...r....\n........\n...N....\n........\n........\nK.......";
            var board = Setup(terrain, pieces, 0, PieceType.King, PieceType.Knight);
            var knight = board.PieceAt(C("d4"));

            CollectionAssert.DoesNotContain(MoveGenerator.Generate(board, Side.Black).Select(m => m.To).ToList(), C("a1"),
                "the friend never attacks the king in stage 3");
            Assert.IsFalse(MoveGenerator.InCheck(board, Side.White));

            board.Pass();
            var result = board.Apply(new Move(board.PieceAt(C("d6")).Id, C("d6"), C("d4")));
            Assert.AreSame(knight, result.KnockedBack);
            Assert.AreEqual(C("d2"), result.KnockTo, "knocked two squares, straight into the lava");
            Assert.IsTrue(result.KnockedIntoLava);
            Assert.IsNull(result.Captured);
            Assert.IsFalse(knight.OnBoard);
            Assert.IsTrue(knight.IsRevivable, "burned in the lava, but it can be revived");
        }

        [Test]
        public void Knockback_IsImpossibleWhenBoxedIn_SoTheAttackIsNotAllowed()
        {
            const string pieces = "........\n........\n........\n........\n........\n......n.\n......**\n....K.**";
            // 我方位置顺序：g1、h1、g2、h2 → 车在 g1，兵在 h1、g2、h2
            var board = Setup(Flat, pieces, 0, PieceType.King, PieceType.Pawn, PieceType.Pawn, PieceType.Pawn, PieceType.Rook);
            var friendMoves = MoveGenerator.Generate(board, Side.Black).Select(m => m.To).ToList();
            CollectionAssert.DoesNotContain(friendMoves, C("h1"), "h1 is boxed in, so it can't be knocked back");
            CollectionAssert.Contains(friendMoves, C("h5"), "the knight still has other moves");

            board.Pass();
            Assert.Throws<InvalidOperationException>(() => board.Apply(new Move(board.PieceAt(C("g3")).Id, C("g3"), C("h1"))));
        }

        [Test]
        public void RevivingInStage3_PlacesNearTheKingOnThePlayersSide()
        {
            var board = Board.CreateStandard();
            var knight = board.PieceAt(C("b1"));
            board.RemovePiece(knight);
            board.ApplyLayout(Stage3Layout.Default, Side.White, 3);
            board.RemovePiece(board.PieceAt(C("d1"))); // 腾出位置
            var square = RevivalPlacement.FindSquare(board, knight).Value;
            Assert.IsTrue(board.InPlayerZone(square));
            Assert.AreEqual(0, board.Height(square));
            Assert.LessOrEqual(square.DistanceTo(board.King(Side.White).Pos), 1);
        }

        [Test]
        public void FriendCastsItsScheduledSkill_OnThePlayersPieces()
        {
            var game = new ChessGame(new GameSettings { Seed = 3, StageTurns = new int[0], StormTurn = 1, FriendSkillTurns = new[] { 0 } });
            Assert.IsTrue(game.InStage3);
            if (game.Phase == GamePhase.ChoosingRule) game.SkipOffers();
            game.DrainEvents();
            game.PlayMove(game.LegalMoves().First());
            var events = game.DrainEvents();
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.OpponentSkill && e.Rule.Id == "wizard_birds"));
            Assert.Greater(game.Board.PiecesOf(Side.White).Count(p => p.FrozenSkin == "poop"), 0);
            Assert.IsTrue(game.Board.PiecesOf(Side.Black).All(p => !p.IsFrozen));
        }

        [Test]
        public void Stage3_NeverEndsInALoss()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var auto = new GameFlowTests.AutoPlayer(seed, null, 80,
                    new GameSettings { StageTurns = new int[0], StormTurn = 1 });
                auto.PlayToEnd();
                Assert.AreNotEqual(Side.Black, auto.Game.Winner);
            }
        }
    }
}
