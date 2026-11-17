using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    /// <summary>蛋糕争夺：落下、谁都能吃、会跑、没抢到的一方后退、之后的选卡和风暴往后排。</summary>
    public class CakeTests
    {
        static Coord C(string s) => new Coord(s[0] - 'a', s[1] - '1');

        static GameSettings CakeSettings(int seed) => new GameSettings { Seed = seed, CakeTurn = 1 };

        [Test]
        public void TheCake_DropsInTheMiddle_AndTheQueensGetTheirCostumes()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var game = new ChessGame(CakeSettings(seed));
                var events = game.DrainEvents();
                Assert.IsTrue(game.CakeActive);
                var cake = game.Board.Cake.Value;
                CollectionAssert.Contains(new[] { C("d4"), C("e4"), C("d5"), C("e5") }, cake);
                Assert.IsTrue(events.Any(e => e.Type == GameEventType.CakeDropped && e.To == cake));
                Assert.IsTrue(events.Any(e => e.Type == GameEventType.Dialogue && e.Text == "Let them eat cake!"));

                var white = game.Board.PiecesOf(Side.White).First(p => p.Type == PieceType.Queen).Skin;
                var black = game.Board.PiecesOf(Side.Black).First(p => p.Type == PieceType.Queen).Skin;
                CollectionAssert.AreEquivalent(new[] { ChessGame.MarieSkin, ChessGame.CleopatraSkin }, new[] { white, black });
                Assert.AreEqual(white, game.QueenSkin(Side.White));
            }
        }

        [Test]
        public void TheCake_CanBeTakenByAnyPiece_AndBlocksTheWay()
        {
            var board = new Board();
            var rook = board.AddPiece(PieceType.Rook, Side.White, C("a4"));
            var pawnSide = board.AddPiece(PieceType.Pawn, Side.White, C("e3"));
            var pawnFront = board.AddPiece(PieceType.Pawn, Side.Black, C("d5"));
            board.AddPiece(PieceType.King, Side.White, C("a1"));
            board.AddPiece(PieceType.King, Side.Black, C("h8"));
            board.PlaceCake(C("d4"));

            var rookMoves = MoveGenerator.MovesFor(board, rook).Select(m => m.To).ToList();
            CollectionAssert.Contains(rookMoves, C("d4"), "sliding onto the cake takes it");
            CollectionAssert.DoesNotContain(rookMoves, C("e4"), "the cake blocks the slide");
            CollectionAssert.Contains(MoveGenerator.MovesFor(board, pawnSide).Select(m => m.To).ToList(), C("d4"), "pawns take it diagonally");
            Assert.IsFalse(board.IsEmpty(C("d4")));

            var black = board.Clone();
            black.Pass();
            CollectionAssert.DoesNotContain(MoveGenerator.MovesFor(black, black.GetPiece(pawnFront.Id)).Select(m => m.To).ToList(), C("d4"),
                "pawns can't walk straight into it");

            var result = board.Apply(new Move(rook.Id, C("a4"), C("d4")));
            Assert.IsTrue(result.CakeTaken);
            Assert.IsFalse(board.Cake.HasValue);
        }

        [Test]
        public void TakingTheCake_MakesTheOtherSideStepBack()
        {
            var game = new ChessGame(CakeSettings(3));
            game.DrainEvents();
            var board = game.Board;
            board.MoveCake(C("c3"));
            var pawn = board.PieceAt(C("e7"));
            board.Relocate(pawn, C("e5"));
            var knight = board.PieceAt(C("g8"));
            board.Relocate(knight, C("f6"));
            var blockedPawn = board.PieceAt(C("a7"));

            var take = game.LegalMoves().First(m => m.From == C("b1") && m.To == C("c3"));
            game.PlayMove(take);
            var events = game.DrainEvents();

            Assert.IsTrue(events.Any(e => e.Type == GameEventType.CakeTaken && e.Side == Side.White));
            Assert.IsFalse(game.CakeActive);
            Assert.AreEqual(C("e6"), pawn.Pos, "the black pawn steps back");
            Assert.AreEqual(C("f6"), knight.Pos, "f7 is taken, so the knight stays");
            Assert.AreEqual(C("a7"), blockedPawn.Pos, "pawns never step back onto the back rank");
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.SteppedBack && e.PieceId == pawn.Id && e.To == C("e6")));
            Assert.IsTrue(board.PiecesOf(Side.White).All(p => p.Pos == p.StartPos || p.Id == take.PieceId), "the winner doesn't move");
        }

        [Test]
        public void TheCake_RunsAStepEachRound_AndEscapesAfterTheLimit()
        {
            var settings = CakeSettings(5);
            settings.CakeMaxRounds = 3;
            var game = new ChessGame(settings);
            game.DrainEvents();
            var ai = new ChessAI(new Random(1)) { BlunderChance = 0 };
            int moves = 0, escapes = 0;
            for (int i = 0; i < 6 && game.CakeDoneTurn < 0; i++)
            {
                // 两边都走一步不吃蛋糕的棋
                var move = game.LegalMoves().FirstOrDefault(m => !game.Board.IsCake(m.To));
                game.PlayMove(move);
                var reply = game.LegalMoves().FirstOrDefault(m => !game.Board.IsCake(m.To));
                game.PlayMove(reply);
                var events = game.DrainEvents();
                moves += events.Count(e => e.Type == GameEventType.CakeMoved);
                escapes += events.Count(e => e.Type == GameEventType.CakeEscaped);
            }
            Assert.Greater(moves, 0, "the cake runs");
            Assert.AreEqual(1, escapes, "nobody took it, so it ran off");
            Assert.AreEqual(1 + settings.CakeMaxRounds, game.CakeDoneTurn);
            Assert.IsFalse(game.CakeActive);
        }

        [Test]
        public void AfterTheCake_TheCardComesFiveTurnsLater_AndTheStormTenAfterThat()
        {
            int reachedStorm = 0, takenByWhite = 0, takenByBlack = 0, escaped = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                var settings = new GameSettings { CakeTurn = 10 };
                var auto = new GameFlowTests.AutoPlayer(seed, null, 80, settings);
                var game = auto.Game;
                for (int i = 0; i < 2000 && game.Phase != GamePhase.GameOver && !game.InStage3; i++)
                {
                    Assert.IsFalse(game.StageCardTurn >= 0 && game.CakeDoneTurn < 0, "no card before the cake is decided");
                    auto.Step();
                }
                if (game.CakeDropTurn >= 0) Assert.AreEqual(10, game.CakeDropTurn);
                var taken = auto.Events.FirstOrDefault(e => e.Type == GameEventType.CakeTaken);
                if (taken != null) { if (taken.Side == Side.White) takenByWhite++; else takenByBlack++; }
                if (auto.Events.Any(e => e.Type == GameEventType.CakeEscaped)) escaped++;
                if (game.StageCardTurn >= 0)
                    Assert.GreaterOrEqual(game.StageCardTurn, game.CakeDoneTurn + settings.CardDelayAfterCake);
                if (game.InStage3)
                {
                    reachedStorm++;
                    // 没到选卡就将死了朋友也会掀起风暴；有选卡时风暴不会晚于选卡后第 10 轮
                    if (game.StageCardTurn >= 0)
                        Assert.LessOrEqual(game.Stage3StartTurn, game.StageCardTurn + settings.StormDelayAfterCard + 1);
                }
            }
            TestContext.Progress.WriteLine($"cake: white took it {takenByWhite}, black took it {takenByBlack}, it ran off {escaped}; {reachedStorm} games reached the storm");
            Assert.Greater(reachedStorm, 0);
        }
    }
}
