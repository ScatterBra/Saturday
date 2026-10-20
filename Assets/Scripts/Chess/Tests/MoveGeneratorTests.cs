using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class MoveGeneratorTests
    {
        static Coord C(string square) => new Coord(square[0] - 'a', square[1] - '1');

        /// <summary>只有两个王的空棋盘，方便摆局面。</summary>
        static Board EmptyBoard()
        {
            var board = new Board();
            board.AddPiece(PieceType.King, Side.White, C("e1"));
            board.AddPiece(PieceType.King, Side.Black, C("e8"));
            return board;
        }

        [Test]
        public void StandardPosition_Has20Moves()
        {
            var board = Board.CreateStandard();
            Assert.AreEqual(20, MoveGenerator.Generate(board, Side.White).Count);
            Assert.AreEqual(20, MoveGenerator.Generate(board, Side.Black).Count);
        }

        [Test]
        public void NoCastling()
        {
            var board = EmptyBoard();
            board.AddPiece(PieceType.Rook, Side.White, C("h1"));
            var king = board.PieceAt(C("e1"));
            var targets = MoveGenerator.MovesFor(board, king).Select(m => m.To).ToList();
            CollectionAssert.DoesNotContain(targets, C("g1"));
            Assert.AreEqual(5, targets.Count);
        }

        [Test]
        public void PawnDoubleStepOnlyFromStartRank()
        {
            var board = EmptyBoard();
            var start = board.AddPiece(PieceType.Pawn, Side.White, C("a2"));
            var advanced = board.AddPiece(PieceType.Pawn, Side.White, C("c3"));
            Assert.AreEqual(2, MoveGenerator.MovesFor(board, start).Count);
            Assert.AreEqual(1, MoveGenerator.MovesFor(board, advanced).Count);
        }

        [Test]
        public void PawnPromotesToQueen()
        {
            var board = EmptyBoard();
            var pawn = board.AddPiece(PieceType.Pawn, Side.White, C("a7"));
            var result = board.Apply(new Move(pawn.Id, C("a7"), C("a8")));
            Assert.IsTrue(result.Promoted);
            Assert.AreEqual(PieceType.Queen, pawn.Type);
        }

        [Test]
        public void TheKingIsNeverCaptured()
        {
            var board = EmptyBoard();
            var rook = board.AddPiece(PieceType.Rook, Side.White, C("e5"));
            var targets = MoveGenerator.MovesFor(board, rook).Select(m => m.To).ToList();
            CollectionAssert.DoesNotContain(targets, C("e8"));
            CollectionAssert.Contains(targets, C("e7"));
        }

        [Test]
        public void PinnedPiecesStay_AndChecksMustBeAnswered()
        {
            var board = EmptyBoard();
            board.AddPiece(PieceType.Rook, Side.Black, C("e7"));
            var bishop = board.AddPiece(PieceType.Bishop, Side.White, C("e2"));
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, bishop).Count, "the bishop is pinned to its king");

            board = EmptyBoard();
            board.AddPiece(PieceType.Rook, Side.Black, C("e5"));
            var knight = board.AddPiece(PieceType.Knight, Side.White, C("b1"));
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, knight).Count, "no knight move answers the check");
            var king = board.King(Side.White);
            CollectionAssert.AreEquivalent(new[] { C("d1"), C("d2"), C("f1"), C("f2") },
                MoveGenerator.MovesFor(board, king).Select(m => m.To), "the king can't stay on the e-file");
        }

        [Test]
        public void Checkmate_IsCheckWithNoLegalMoves()
        {
            var board = new Board();
            board.AddPiece(PieceType.King, Side.White, C("h1"));
            board.AddPiece(PieceType.Pawn, Side.White, C("g2"));
            board.AddPiece(PieceType.Pawn, Side.White, C("h2"));
            board.AddPiece(PieceType.King, Side.Black, C("e8"));
            board.AddPiece(PieceType.Rook, Side.Black, C("a1"));
            Assert.IsTrue(MoveGenerator.IsCheckmated(board, Side.White));
            Assert.IsFalse(MoveGenerator.IsCheckmated(board, Side.Black));
        }

        [Test]
        public void FrozenPieceCannotMove()
        {
            var board = EmptyBoard();
            var knight = board.AddPiece(PieceType.Knight, Side.White, C("d4"));
            knight.FrozenTurns = 2;
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, knight).Count);
        }

        [Test]
        public void FreezeCountsDownOnOwnersTurnsOnly()
        {
            var board = EmptyBoard();
            var blackPawn = board.AddPiece(PieceType.Pawn, Side.Black, C("a7"));
            blackPawn.FrozenTurns = 2;
            blackPawn.FrozenSkin = "tree";
            board.Pass(); // 白方回合结束，黑方的冻结不变
            Assert.AreEqual(2, blackPawn.FrozenTurns);
            board.Pass(); // 黑方回合结束
            Assert.AreEqual(1, blackPawn.FrozenTurns);
            board.Pass();
            board.Pass();
            Assert.AreEqual(0, blackPawn.FrozenTurns);
            Assert.IsNull(blackPawn.FrozenSkin);
        }

        [Test]
        public void LavaBlocksStepsAndSlides()
        {
            var board = EmptyBoard();
            var rook = board.AddPiece(PieceType.Rook, Side.White, C("a1"));
            board.SetLava(C("a3"));
            var targets = MoveGenerator.MovesFor(board, rook).Select(m => m.To).ToList();
            CollectionAssert.Contains(targets, C("a2"));
            CollectionAssert.DoesNotContain(targets, C("a3"));
            CollectionAssert.DoesNotContain(targets, C("a4"));
        }

        [Test]
        public void KnightJumpsOverLavaButCannotLandOnIt()
        {
            var board = EmptyBoard();
            var knight = board.AddPiece(PieceType.Knight, Side.White, C("b1"));
            board.SetLava(C("b2"));
            board.SetLava(C("c3"));
            var targets = MoveGenerator.MovesFor(board, knight).Select(m => m.To).ToList();
            CollectionAssert.Contains(targets, C("a3"));
            CollectionAssert.DoesNotContain(targets, C("c3"));
        }

        [Test]
        public void InCheck_DetectsAttackedKing_FrozenAttackerDoesNotCount()
        {
            var board = EmptyBoard();
            Assert.IsFalse(MoveGenerator.InCheck(board, Side.White));
            var rook = board.AddPiece(PieceType.Rook, Side.Black, C("e5"));
            Assert.IsTrue(MoveGenerator.InCheck(board, Side.White));
            rook.FrozenTurns = 1;
            Assert.IsFalse(MoveGenerator.InCheck(board, Side.White));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var board = Board.CreateStandard();
            var copy = board.Clone();
            var move = MoveGenerator.Generate(copy, Side.White)[0];
            copy.Apply(move);
            Assert.AreEqual(Side.White, board.SideToMove);
            Assert.IsNotNull(board.PieceAt(move.From));
            Assert.AreEqual(20, MoveGenerator.Generate(board, Side.White).Count);
        }
    }
}
