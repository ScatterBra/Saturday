using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class Stage3Tests
    {
        static Coord C(string square) => new Coord(square[0] - 'a', square[1] - '1');

        const string Flat =
            "........\n........\n........\n........\n........\n........\n........\n........";

        /// <summary>
        /// 摆好指定的棋子（"Ke1" 大写是玩家/白方，"rd6" 小写是朋友/黑方），再铺上第三阶段的地形，轮到玩家。
        /// lava 是额外的岩浆格。
        /// </summary>
        static Board Setup(string terrain, string[] pieces, params string[] lava)
        {
            var board = new Board();
            foreach (var s in pieces)
            {
                char letter = s[0];
                var side = char.IsUpper(letter) ? Side.White : Side.Black;
                var type = "PNBRQK".IndexOf(char.ToUpper(letter)) switch
                {
                    0 => PieceType.Pawn, 1 => PieceType.Knight, 2 => PieceType.Bishop,
                    3 => PieceType.Rook, 4 => PieceType.Queen, _ => PieceType.King,
                };
                board.AddPiece(type, side, C(s.Substring(1)));
            }
            board.BeginStage3(Stage3Layout.Parse(terrain), Side.White);
            foreach (var l in lava) board.SetLava(C(l));
            return board;
        }

        static RuleContext Context(Board board, int seed, List<GameEvent> events = null) =>
            new RuleContext(board, new Random(seed), Side.White, e => events?.Add(e));

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
            Assert.AreEqual(0, layout.Lava.Count(l => l), "lava is scattered at random, not part of the terrain");

            Assert.DoesNotThrow(() => Stage3Layout.Parse(Flat.Replace('.', '_')), "the old '_' still reads as flat ground");
            Assert.Throws<FormatException>(() => Stage3Layout.Parse("........"));
            Assert.Throws<FormatException>(() => Stage3Layout.Parse("....x...\n" + Flat.Substring(9)));
        }

        [Test]
        public void TheStorm_ScattersBothArmies_KeepsTheirLooks_AndScattersFiveLavaSquares()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var board = Board.CreateStandard();
                var lostKnight = board.PieceAt(C("b1"));
                board.RemovePiece(lostKnight);
                var rook = board.PieceAt(C("h1"));
                rook.Skin = "frog";
                var pawn = board.PieceAt(C("a7"));
                pawn.FrozenTurns = 2;
                pawn.FrozenSkin = "tree";

                board.BeginStage3(Stage3Layout.Default, Side.White);
                board.Scatter(new Random(seed), 5);

                Assert.IsTrue(board.Stage3);
                Assert.AreEqual(Side.White, board.SideToMove);
                Assert.AreEqual(15, board.PiecesOf(Side.White).Count(), "nobody is left out");
                Assert.AreEqual(16, board.PiecesOf(Side.Black).Count(), "the friend keeps its whole army");
                Assert.IsFalse(lostKnight.OnBoard, "captured pieces stay captured");
                Assert.AreEqual("frog", rook.Skin);
                Assert.AreEqual(2, pawn.FrozenTurns, "effects carry over");

                var lava = Enumerable.Range(0, 64).Select(Coord.FromIndex).Where(board.IsLava).ToList();
                Assert.AreEqual(5, lava.Count);
                var steps = board.KnightStepsToCastle();
                var whiteKing = board.King(Side.White);
                foreach (var p in board.AllPieces.Where(p => p.OnBoard))
                {
                    Assert.IsFalse(board.IsLava(p.Pos) || board.IsCastle(p.Pos), $"{p} landed on lava or the castle");
                    Assert.AreSame(p, board.PieceAt(p.Pos));
                    if (p.Type == PieceType.Pawn) Assert.That(p.Pos.Y, Is.InRange(1, 6), $"{p} on the first or last rank");
                    if (p.Side == Side.Black) Assert.GreaterOrEqual(p.Pos.Y, 4, $"{p} should guard the castle half");
                    if (p.Side == Side.White && p.Type == PieceType.Knight)
                        Assert.GreaterOrEqual(steps[p.Pos.Index], Board.MinKnightStepsToCastle, $"{p} is too close to the castle");
                    if (p.Type == PieceType.King)
                        Assert.IsFalse(board.AllPieces.Any(o => o.OnBoard && o.Side != p.Side && o.Pos.DistanceTo(p.Pos) <= 1),
                            $"{p} starts right next to an enemy");
                }
                Assert.IsFalse(MoveGenerator.InCheck(board, Side.Black), "the friend's king doesn't start in check");
                foreach (var c in lava)
                {
                    Assert.Greater(c.DistanceTo(whiteKing.Pos), 1, "no lava right next to the player's king");
                    Assert.IsFalse(Enumerable.Range(0, 64).Select(Coord.FromIndex).Any(x => board.IsCastle(x) && x.DistanceTo(c) <= 1),
                        "no lava right next to the castle");
                }
                Assert.GreaterOrEqual(lava.Count(c => board.PiecesOf(Side.Black).Any(p => p.Pos.DistanceTo(c) <= 1)), 2,
                    "some lava lies next to the friend's pieces");
            }
        }

        [Test]
        public void TheStorm_InAGame_MovesEveryoneAndTalksAboutTheLava()
        {
            var game = new ChessGame(new GameSettings { Seed = 5, StageTurns = new int[0], StormTurn = 1, FriendSkillTurns = new int[0] });
            Assert.IsTrue(game.InStage3);
            Assert.AreEqual(16, game.Board.PiecesOf(Side.White).Count());
            Assert.AreEqual(16, game.Board.PiecesOf(Side.Black).Count());
            Assert.AreEqual(new GameSettings().LavaSquares, Enumerable.Range(0, 64).Count(i => game.Board.IsLava(Coord.FromIndex(i))));
            Assert.IsTrue(game.DrainEvents().Any(e => e.Type == GameEventType.Dialogue && e.Text.Contains("lava")));
        }

        [Test]
        public void LavaBlocksEveryone_KnightsJumpOverIt()
        {
            var board = Setup(Flat, new[] { "Ke1", "Ra1", "Nb1", "kh8" }, "a3", "c3");
            CollectionAssert.AreEquivalent(new[] { C("a2") }, Targets(board, "a1").Where(c => c.X == 0), "the rook stops before the lava");
            var knight = Targets(board, "b1");
            CollectionAssert.DoesNotContain(knight, C("a3"));
            CollectionAssert.DoesNotContain(knight, C("c3"));
            CollectionAssert.Contains(knight, C("d2"));
        }

        const string PlateauTerrain =
            "C2......\n21......\n1.1.....\n........\n........\n........\n........\n........";

        [Test]
        public void Plateau_IsOnlyForLooks_AndKnightsGoStraightIntoTheCastle()
        {
            var board = Setup(PlateauTerrain, new[] { "Ke1", "Na5", "Rb8", "Rc1", "kh8" });
            CollectionAssert.AreEquivalent(new[] { C("b7"), C("c6"), C("c4"), C("b3") }, Targets(board, "a5"));

            var rookTargets = Targets(board, "b8");
            CollectionAssert.DoesNotContain(rookTargets, C("a8"), "only knights may enter the castle");
            CollectionAssert.IsSubsetOf(new[] { C("c8"), C("g8"), C("b7"), C("b1") }, rookTargets,
                "a rook on the plateau slides as usual, up and down any height");
            CollectionAssert.IsSubsetOf(new[] { C("c6"), C("c8") }, Targets(board, "c1"), "the plateau doesn't block sliding");

            Play(board, "a5", "c4");
            Play(board, "c4", "b6");
            CollectionAssert.Contains(Targets(board, "b6"), C("a8"), "no task to finish first");
            Assert.IsTrue(Play(board, "b6", "a8").Rescued);
        }

        [Test]
        public void FriendKnocksPiecesBack_PreferringLava_AndNeverAttacksTheKing()
        {
            var board = Setup(Flat, new[] { "Ka1", "Nd4", "ra8", "rd6", "kh8" }, "d2");
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
            var board = Setup(Flat, new[] { "Ke1", "Rg1", "Ph1", "Pg2", "Ph2", "ng3", "ka8" });
            var friendMoves = MoveGenerator.Generate(board, Side.Black).Select(m => m.To).ToList();
            CollectionAssert.DoesNotContain(friendMoves, C("h1"), "h1 is boxed in, so it can't be knocked back");
            CollectionAssert.Contains(friendMoves, C("h5"), "the knight still has other moves");

            board.Pass();
            Assert.Throws<InvalidOperationException>(() => board.Apply(new Move(board.PieceAt(C("g3")).Id, C("g3"), C("h1"))));
        }

        static void Effect(Board board, string rook, string skin, int seed, List<GameEvent> events = null, int? preferred = null)
        {
            var piece = board.PieceAt(C(rook));
            piece.Skin = skin;
            var ctx = Context(board, seed, events);
            ctx.PreferredTargetId = preferred;
            new FireHydrantRule().OnPieceMoved(ctx, piece, piece.Pos);
        }

        [Test]
        public void InStage3_HydrantsSprayPiecesIntoTheLava_ButNeverTheKing()
        {
            var board = Setup(Flat, new[] { "Ke1", "Rd4", "pd5", "kh8" }, "d6");
            var pawn = board.PieceAt(C("d5"));
            var events = new List<GameEvent>();
            Effect(board, "d4", "hydrant", 1, events);
            Assert.IsFalse(pawn.OnBoard, "sprayed one square into the lava and burned");
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Pushed && e.To == C("d6")));
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Captured && e.Text == "lava" && e.PieceId == pawn.Id));

            board = Setup(Flat, new[] { "Ke1", "Rd4", "kd5" }, "d6");
            Effect(board, "d4", "hydrant", 1);
            Assert.AreEqual(C("d5"), board.King(Side.Black).Pos, "the king can't be sprayed into the lava, so it stays put");
        }

        [Test]
        public void InStage3_FrogsPullEnemiesIntoTheLava()
        {
            var board = Setup(Flat, new[] { "Ke1", "Rd4", "nd6", "kh8" }, "d5");
            var knight = board.PieceAt(C("d6"));
            var options = FireHydrantRule.EffectCandidates(board, Skinned(board, "d4", "frog"));
            Assert.AreEqual(1, options.Count);
            Assert.IsTrue(options[0].Burns, "the preview knows it lands in the lava");
            Effect(board, "d4", "frog", 1);
            Assert.IsFalse(knight.OnBoard);
        }

        static Piece Skinned(Board board, string square, string skin)
        {
            var piece = board.PieceAt(C(square));
            piece.Skin = skin;
            return piece;
        }

        [Test]
        public void ThePreviewedTarget_IsTheOneThatGetsSprayed()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var board = Setup(Flat, new[] { "Ke1", "Rd4", "pd5", "pc4", "ka8" }, "d6", "b4");
                var left = board.PieceAt(C("c4"));
                Effect(board, "d4", "hydrant", seed, preferred: left.Id);
                Assert.IsFalse(left.OnBoard, $"seed {seed}: the previewed pawn on c4 goes into the lava at b4");
                Assert.IsNotNull(board.PieceAt(C("d5")), "the other pawn is left alone");
            }
        }

        [Test]
        public void RevivingInStage3_OnlyOnThePlayersHalf_NeverOnLavaOrTheCastle()
        {
            var board = Setup(Flat, new[] { "Ke4", "Nb1", "ka8" }, "c2");
            var knight = board.PieceAt(C("b1"));
            board.RemovePiece(knight);
            var squares = RevivalPlacement.Squares(board, knight);
            Assert.IsTrue(squares.All(c => c.Y <= 3));
            CollectionAssert.DoesNotContain(squares, C("c2"), "not on lava");
            CollectionAssert.DoesNotContain(squares, C("e4"), "not on an occupied square");
            CollectionAssert.Contains(squares, C("b1"));
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
