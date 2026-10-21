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
            Assert.AreEqual(4, layout.Heights[C("b7").Index]);
            var lava = new[] { "h4", "g4", "g3", "f3", "e3", "d3", "c3", "c2", "b2", "b1", "a1" };
            CollectionAssert.AreEquivalent(lava.Select(C), Enumerable.Range(0, 64).Where(i => layout.Lava[i]).Select(Coord.FromIndex));
            Assert.IsTrue(layout.PlayerZone[C("h3").Index], "gap in the river");
            Assert.AreEqual(12, layout.PlayerZone.Count(z => z));
            Assert.AreEqual(12, layout.FriendPieces.Count);
            Assert.AreEqual(C("f1"), layout.PlayerKing);
            CollectionAssert.AreEqual(new[] { C("e2"), C("h2") }, layout.KnightSlots);
            Assert.IsTrue(layout.OtherSlots.All(c => layout.PlayerZone[c.Index]), "every slot is on the player's side");
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
            Assert.AreEqual(C("f1"), board.King(Side.White).Pos);
            Assert.AreEqual(PieceType.Knight, board.PieceAt(C("e2")).Type, "the remaining knight goes to the first knight slot");
            foreach (var p in board.PiecesOf(Side.White))
            {
                Assert.IsFalse(board.IsLava(p.Pos), $"{p} placed on lava");
                Assert.AreEqual(0, board.Height(p.Pos), $"{p} placed on the plateau");
            }
            Assert.AreEqual(3, board.PiecesOf(Side.White).Count(p => !board.InPlayerZone(p.Pos)),
                "15 pieces, 12 squares on the player's side: the rest go to the nearest free ground");
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
        public void KnightsJumpNormally_AndAfterTheTaskEveryoneCanStandOnAndCrossLava()
        {
            const string pieces = "........\n........\n........\n........\n........\n........\n*N**....\n....K..*";
            var board = Setup(RiverTerrain, pieces, 3,
                PieceType.King, PieceType.Knight, PieceType.Rook, PieceType.Pawn, PieceType.Pawn, PieceType.Pawn);
            // 我方位置顺序：h1、a2、c2、d2 → 车在 h1，兵在 a2、c2、d2，马在 b2，王在 e1
            var before = Targets(board, "b2");
            CollectionAssert.Contains(before, C("d3"), "a knight may jump into the lava to sacrifice itself");
            CollectionAssert.Contains(before, C("d1"));
            CollectionAssert.DoesNotContain(before, C("a4"), "before the task the far side is closed");
            CollectionAssert.DoesNotContain(before, C("c4"));

            Play(board, "a2", "a3");
            Play(board, "c2", "c3");
            var last = Play(board, "d2", "d3");
            Assert.IsTrue(last.TaskCompleted);
            Assert.IsTrue(board.TaskComplete);

            CollectionAssert.AreEquivalent(new[] { C("a4"), C("c4"), C("d3"), C("d1") }, Targets(board, "b2"),
                "an ordinary knight jump, lava and the far side included");

            Play(board, "h1", "h3");
            CollectionAssert.IsSubsetOf(new[] { C("g3"), C("d3"), C("a3"), C("h8") }, Targets(board, "h3"),
                "the rook slides over the lava and across the river");
            var result = Play(board, "h3", "e3");
            Assert.IsFalse(result.Sacrificed, "after the task lava is just ground");
            Assert.AreEqual(PieceType.Rook, board.PieceAt(C("e3")).Type);
        }

        const string PlateauTerrain =
            "C2......\n21......\n1.1.....\n........\n........\n........\n........\n........";

        [Test]
        public void Plateau_IsOnlyForLooks_EveryoneMovesNormally_OnlyKnightsEnterTheCastle()
        {
            const string pieces = ".*......\n........\n........\nN.......\n........\n........\n........\n..*.K...";
            var board = Setup(PlateauTerrain, pieces, 0, PieceType.King, PieceType.Knight, PieceType.Rook, PieceType.Rook);
            // 我方位置顺序：c1、b8 → 两个车在 c1 和 b8，马在 a5

            CollectionAssert.AreEquivalent(new[] { C("b7"), C("c6"), C("c4"), C("b3") }, Targets(board, "a5"));

            var rookTargets = Targets(board, "b8");
            CollectionAssert.DoesNotContain(rookTargets, C("a8"), "only knights may enter the castle");
            CollectionAssert.IsSubsetOf(new[] { C("c8"), C("h8"), C("b7"), C("b1") }, rookTargets,
                "a rook on the plateau slides as usual, up and down any height");

            CollectionAssert.IsSubsetOf(new[] { C("c6"), C("c8") }, Targets(board, "c1"), "the plateau doesn't block sliding");

            Play(board, "a5", "c4");
            Play(board, "c4", "b6");
            CollectionAssert.Contains(Targets(board, "b6"), C("a8"));
            var result = Play(board, "b6", "a8");
            Assert.IsTrue(result.Rescued);
        }

        [Test]
        public void TheCastleStaysShut_UntilTheLavaTaskIsDone()
        {
            const string pieces = "........\n........\n.N......\n........\n........\n........\n........\n....K...";
            var board = Setup(PlateauTerrain.Replace('.', '_'), pieces, 1, PieceType.King, PieceType.Knight);
            var targets = Targets(board, "b6");
            CollectionAssert.DoesNotContain(targets, C("a8"));
            CollectionAssert.Contains(targets, C("d5"), "the knight can still jump elsewhere");
        }

        [Test]
        public void UndyingPieces_KnockedIntoLava_SitOutOneTurnInsteadOfDying()
        {
            const string terrain = "........\n........\n........\n........\n........\n........\n...~....\n........";
            const string pieces = "r.......\n........\n...r....\n........\n...N....\n........\n........\nK.......";
            var board = Setup(terrain, pieces, 0, PieceType.King, PieceType.Knight);
            var knight = board.PieceAt(C("d4"));
            knight.Undying = true;

            board.Pass();
            var result = board.Apply(new Move(board.PieceAt(C("d6")).Id, C("d6"), C("d4")));
            Assert.AreSame(knight, result.KnockedBack);
            Assert.AreEqual(C("d2"), result.KnockTo, "the friend still likes knocking it into the lava");
            Assert.IsTrue(result.Scorched);
            Assert.IsFalse(result.KnockedIntoLava);
            Assert.IsTrue(knight.OnBoard);
            Assert.AreSame(knight, board.PieceAt(C("d2")));
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, knight).Count, "it can't move this turn");

            board.Pass(); // 玩家这一回合动不了
            Assert.IsFalse(knight.IsFrozen);
            Assert.Greater(MoveGenerator.MovesFor(board, knight).Count, 0);
        }

        [Test]
        public void EveryTwoSacrifices_ThePlayerPicksAnUndyingPiece()
        {
            var game = new ChessGame(new GameSettings { Seed = 5, StageTurns = new int[0], StormTurn = 1, FriendSkillTurns = new int[0] });
            Assert.IsTrue(game.InStage3);
            int sacrifices = 0, guard = 0;
            while (sacrifices < 2)
            {
                Assert.Less(++guard, 100);
                switch (game.Phase)
                {
                    case GamePhase.ChoosingRule:
                        game.SkipOffers();
                        break;
                    case GamePhase.PlayerTurn:
                        game.PlayMove(game.LegalMoves().First(m => game.Board.IsLava(m.To)));
                        sacrifices++;
                        if (sacrifices == 1) Assert.AreEqual(GamePhase.OpponentTurn, game.Phase, "one sacrifice is not enough yet");
                        break;
                    case GamePhase.OpponentTurn:
                        game.PlayMove(game.LegalMoves().First());
                        break;
                    default:
                        Assert.Fail($"unexpected phase {game.Phase}");
                        break;
                }
            }
            Assert.AreEqual(2, game.Board.Sacrifices);
            Assert.AreEqual(GamePhase.ChoosingTarget, game.Phase);
            var request = game.PendingTarget;
            Assert.IsFalse(request.AllowSkip);
            Assert.IsTrue(request.Options.All(c => game.Board.PieceAt(c).Side == Side.White && game.Board.PieceAt(c).Type != PieceType.King));
            var chosen = game.Board.PieceAt(request.Options[0]);
            game.ResolveTarget(request.Options[0]);
            Assert.IsTrue(chosen.Undying);
            Assert.AreEqual(GamePhase.OpponentTurn, game.Phase, "after picking, it's the friend's move");
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
            board.RemovePiece(board.PieceAt(C("e1"))); // 腾出王旁边的位置
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
