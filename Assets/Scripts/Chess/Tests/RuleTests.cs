using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class RuleTests
    {
        static Coord C(string square) => new Coord(square[0] - 'a', square[1] - '1');

        static RuleContext Context(Board board, int seed, List<GameEvent> events = null) =>
            new RuleContext(board, new Random(seed), Side.White, e => events?.Add(e));

        /// <summary>执行一条不需要点选的规则。</summary>
        static void Run(RuleBase rule, RuleContext ctx)
        {
            foreach (var request in rule.Activate(ctx))
                Assert.Fail($"{rule.Id} unexpectedly requested a target: {request.Prompt}");
        }

        /// <summary>把一方的某些棋子吃掉（直接移出棋盘）。</summary>
        static void Capture(Board board, params string[] squares)
        {
            foreach (var s in squares) board.RemovePiece(board.PieceAt(C(s)));
        }

        [Test]
        public void FireHydrant_ChangesAllRooksLooksOnly()
        {
            var board = Board.CreateStandard();
            Run(new FireHydrantRule(), Context(board, 1));
            var rooks = board.AllPieces.Where(p => p.Type == PieceType.Rook).ToList();
            Assert.AreEqual(4, rooks.Count);
            Assert.IsTrue(rooks.All(r => r.Skin == "hydrant"));
            Assert.AreEqual(20, MoveGenerator.Generate(board, Side.White).Count);
        }

        [Test]
        public void BishopPuns_AdvanceAfterEachFullTurn()
        {
            var board = Board.CreateStandard();
            var ctx = Context(board, 1);
            var rule = new BishopPunsRule();
            Run(rule, ctx);
            var bishop = board.PieceAt(C("c1"));
            Assert.AreEqual("bish_hop", bishop.Skin);
            rule.OnTurnEnded(ctx, Side.White);
            Assert.AreEqual("bish_hop", bishop.Skin);
            rule.OnTurnEnded(ctx, Side.Black);
            Assert.AreEqual("fish_shop", bishop.Skin);
            for (int i = 0; i < 10; i++) rule.OnTurnEnded(ctx, Side.Black);
            Assert.AreEqual("bishblob", bishop.Skin);
        }

        [Test]
        public void TreePawns_FreezeOnlyOpponentPawns()
        {
            var board = Board.CreateStandard();
            Run(new TreePawnsRule(), Context(board, 1));
            Assert.IsTrue(board.PiecesOf(Side.Black).Where(p => p.Type == PieceType.Pawn)
                .All(p => p.FrozenTurns == TreePawnsRule.FreezeTurns && p.FrozenSkin == "tree"));
            Assert.IsTrue(board.PiecesOf(Side.White).All(p => !p.IsFrozen));
            Assert.IsTrue(board.PiecesOf(Side.Black).Where(p => p.Type != PieceType.Pawn).All(p => !p.IsFrozen));
        }

        [Test]
        public void DragonKnight_BurnsOnlyPawnsKnightsBishops_PawnsMostOften()
        {
            var burned = new Dictionary<PieceType, int>();
            for (int seed = 0; seed < 300; seed++)
            {
                var board = Board.CreateStandard();
                var events = new List<GameEvent>();
                var ctx = Context(board, seed, events);
                var rule = new DragonKnightRule();
                Assert.IsTrue(rule.IsAvailable(ctx));
                Run(rule, ctx);

                var burn = events.Single(e => e.Type == GameEventType.Captured);
                Assert.AreEqual("burned", burn.Text);
                var victim = board.GetPiece(burn.PieceId);
                Assert.AreEqual(Side.Black, victim.Side);
                Assert.IsFalse(victim.OnBoard);
                Assert.AreEqual("dragon", board.GetPiece(burn.SourcePieceId).Skin);
                burned[victim.Type] = burned.TryGetValue(victim.Type, out var n) ? n + 1 : 1;
            }
            CollectionAssert.IsSubsetOf(burned.Keys, new[] { PieceType.Pawn, PieceType.Knight, PieceType.Bishop });
            Assert.Greater(burned[PieceType.Pawn], burned[PieceType.Knight] + burned[PieceType.Bishop]);
        }

        [Test]
        public void DragonKnight_UnavailableWithoutKnightsOrTargets()
        {
            var board = Board.CreateStandard();
            Capture(board, "b1", "g1");
            Assert.IsFalse(new DragonKnightRule().IsAvailable(Context(board, 1)));

            board = Board.CreateStandard();
            Capture(board, "a7", "b7", "c7", "d7", "e7", "f7", "g7", "h7", "b8", "c8", "f8", "g8");
            Assert.IsFalse(new DragonKnightRule().IsAvailable(Context(board, 1)));
        }

        [Test]
        public void CrayonRevival_NeverRevivesQueenOrRook()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var board = Board.CreateStandard();
                Capture(board, "d1", "a1", "b1", "e2");
                var events = new List<GameEvent>();
                var rule = new CrayonRevivalRule();
                var ctx = Context(board, seed, events);
                Assert.IsTrue(rule.IsAvailable(ctx));
                Run(rule, ctx);

                var revived = board.GetPiece(events.Single(e => e.Type == GameEventType.Revived).PieceId);
                Assert.That(revived.Type, Is.EqualTo(PieceType.Knight).Or.EqualTo(PieceType.Pawn));
                Assert.AreEqual(revived.StartPos, revived.Pos, "start square was free, so it should return there");
                Assert.AreEqual("crayon", revived.Skin);
                Assert.AreSame(revived, board.PieceAt(revived.Pos));
            }
        }

        [Test]
        public void CrayonRevival_UnavailableWhenOnlyQueenOrRookLost()
        {
            var board = Board.CreateStandard();
            Assert.IsFalse(new CrayonRevivalRule().IsAvailable(Context(board, 1)));
            Capture(board, "d1", "h1");
            Assert.IsFalse(new CrayonRevivalRule().IsAvailable(Context(board, 1)));
        }

        [Test]
        public void CrayonRevival_PawnAvoidsBackRanksWhenStartSquareTaken()
        {
            var board = new Board();
            board.AddPiece(PieceType.King, Side.White, C("e1"));
            board.AddPiece(PieceType.King, Side.Black, C("e8"));
            var pawn = board.AddPiece(PieceType.Pawn, Side.White, C("a2"));
            board.RemovePiece(pawn);
            board.AddPiece(PieceType.Knight, Side.White, C("a2"));
            var square = CrayonRevivalRule.FindSquare(board, pawn).Value;
            Assert.AreEqual(1, square.DistanceTo(C("a2")));
            Assert.AreNotEqual(0, square.Y);
            Assert.AreEqual(C("b2"), square, "same distance: prefer the square closest to home");
        }

        [Test]
        public void RevivePiece_PlayerPicksWhichLostPieceComesBack()
        {
            var board = Board.CreateStandard();
            Capture(board, "b1", "e2", "d1");
            var rule = new RevivePieceRule();
            var ctx = Context(board, 1);
            Assert.IsTrue(rule.IsAvailable(ctx));

            var requests = rule.Activate(ctx).GetEnumerator();
            Assert.IsTrue(requests.MoveNext());
            var request = requests.Current;
            Assert.IsTrue(request.IsPieceRequest);
            Assert.IsTrue(request.AllowSkip);
            var queen = board.AllPieces.Single(p => p.Type == PieceType.Queen && p.Side == Side.White);
            CollectionAssert.Contains(request.PieceOptions, queen.Id, "any lost piece can be picked, even the queen");
            Assert.AreEqual(3, request.PieceOptions.Count);

            request.PieceResult = queen.Id;
            Assert.IsFalse(requests.MoveNext());
            Assert.IsTrue(queen.OnBoard);
            Assert.AreEqual(C("d1"), queen.Pos, "goes back to its start square when it is free");
        }

        [Test]
        public void RevivePiece_UnavailableWithNothingLost_AndSkipKeepsBoard()
        {
            var board = Board.CreateStandard();
            Assert.IsFalse(new RevivePieceRule().IsAvailable(Context(board, 1)));

            Capture(board, "b1");
            var requests = new RevivePieceRule().Activate(Context(board, 1)).GetEnumerator();
            Assert.IsTrue(requests.MoveNext());
            requests.Current.PieceResult = null;
            Assert.IsFalse(requests.MoveNext());
            Assert.IsNull(board.PieceAt(C("b1")));
        }

        [Test]
        public void WizardBirds_CastByFriend_FreezesPlayerPieces()
        {
            var board = Board.CreateStandard();
            var events = new List<GameEvent>();
            var friend = new RuleContext(board, new Random(3), Side.Black, events.Add);
            Assert.IsTrue(friend.CastByFriend);
            Run(new WizardBirdsRule(), friend);
            Assert.AreEqual(WizardBirdsRule.MaxTargets, board.PiecesOf(Side.White).Count(p => p.IsFrozen));
            Assert.IsTrue(board.PiecesOf(Side.Black).All(p => !p.IsFrozen));
            Assert.AreEqual(Speakers.Friend, events.First(e => e.Type == GameEventType.Dialogue).Speaker);
        }

        [Test]
        public void WizardBirds_FreezeUpTo4EnemiesNeverTheKing_QueenRarely()
        {
            int queenHits = 0, pawnHits = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var board = Board.CreateStandard();
                Run(new WizardBirdsRule(), Context(board, seed));
                var frozen = board.PiecesOf(Side.Black).Where(p => p.IsFrozen).ToList();
                Assert.AreEqual(WizardBirdsRule.MaxTargets, frozen.Count);
                Assert.IsTrue(frozen.All(p => p.Type != PieceType.King && p.FrozenSkin == "poop" && p.FrozenTurns == WizardBirdsRule.FreezeTurns));
                Assert.IsTrue(board.PiecesOf(Side.White).All(p => !p.IsFrozen));
                queenHits += frozen.Count(p => p.Type == PieceType.Queen);
                pawnHits += frozen.Count(p => p.Type == PieceType.Pawn);
            }
            Assert.Less(queenHits, pawnHits / 8);
        }

        [Test]
        public void WizardBirds_UnavailableWhenOnlyKingLeft()
        {
            var board = new Board();
            board.AddPiece(PieceType.King, Side.White, C("e1"));
            board.AddPiece(PieceType.King, Side.Black, C("e8"));
            Assert.IsFalse(new WizardBirdsRule().IsAvailable(Context(board, 1)));
        }
    }
}
