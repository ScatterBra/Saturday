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
        public void FireHydrant_ARookIsHydrantHRookIsFrog_OpponentMirrored_MovesUnchanged()
        {
            var board = Board.CreateStandard();
            Run(new FireHydrantRule(), Context(board, 1));
            Assert.AreEqual("hydrant", board.PieceAt(C("a1")).Skin);
            Assert.AreEqual("frog", board.PieceAt(C("h1")).Skin);
            Assert.AreEqual("frog", board.PieceAt(C("a8")).Skin);
            Assert.AreEqual("hydrant", board.PieceAt(C("h8")).Skin);
            Assert.AreEqual(20, MoveGenerator.Generate(board, Side.White).Count);
        }

        [Test]
        public void FireHydrant_UsesTheStartingSquare_NotWhereTheRookIsNow()
        {
            var board = Board.CreateStandard();
            var rook = board.PieceAt(C("h1"));
            board.Relocate(rook, C("c4"));
            Run(new FireHydrantRule(), Context(board, 1));
            Assert.AreEqual("frog", rook.Skin, "the h-rook stays a frog after wandering to the a side");
        }

        static void Tongue(Board board, string frog, int seed = 1, List<GameEvent> events = null)
        {
            var rook = board.PieceAt(C(frog));
            rook.Skin = "frog";
            new FireHydrantRule().OnPieceMoved(Context(board, seed, events), rook, rook.Pos);
        }

        [Test]
        public void Frog_PullsTheClosestEnemyInALineOneStepCloser()
        {
            var board = Setup("Rd4", "pd7", "nf6");
            var events = new List<GameEvent>();
            Tongue(board, "d4", 1, events);
            var knight = board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Knight);
            Assert.AreEqual(C("e5"), knight.Pos, "the diagonal knight two squares away is closer than the pawn three squares up");
            Assert.AreEqual(C("d7"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Pawn).Pos);
            var pull = events.Single(e => e.Type == GameEventType.Pushed);
            Assert.AreEqual(C("f6"), pull.From);
            Assert.AreEqual(C("e5"), pull.To);
        }

        [Test]
        public void Frog_IgnoresFriendsAdjacentPiecesAndBlockedLines()
        {
            // 自己的棋子不拉；紧挨着的敌人拉不动，也挡住后面
            var board = Setup("Rd4", "Pd6", "pe4", "pg4", "pa4");
            Tongue(board, "d4");
            Assert.AreEqual(C("d6"), board.PieceAt(C("d6"))?.Pos, "own pawn stays");
            Assert.IsNotNull(board.PieceAt(C("g4")), "the pawn behind the adjacent one is blocked");
            Assert.IsNull(board.PieceAt(C("a4")), "the open line on the left gets pulled");
            Assert.IsNotNull(board.PieceAt(C("b4")));

            // 超过 3 格够不着
            board = Setup("Rd1", "pd5");
            Tongue(board, "d1");
            Assert.IsNotNull(board.PieceAt(C("d5")));
        }

        [Test]
        public void Frog_DoesNotSpray_AndHydrantDoesNotPull()
        {
            var board = Setup("Rd4", "Pd5");
            Tongue(board, "d4");
            Assert.IsNotNull(board.PieceAt(C("d5")), "a frog never sprays its neighbour");

            board = Setup("Rd4", "pd6");
            Spray(board, "d4");
            Assert.IsNotNull(board.PieceAt(C("d6")), "a hydrant never pulls");
        }

        [Test]
        public void BishopVariants_FourBishopsGetFourDifferentLooks_AndKeepThem()
        {
            var firstSkins = new HashSet<string>();
            for (int seed = 0; seed < 30; seed++)
            {
                var board = Board.CreateStandard();
                var ctx = Context(board, seed);
                var rule = new BishopVariantsRule();
                Run(rule, ctx);
                var skins = board.AllPieces.Where(p => p.Type == PieceType.Bishop).Select(p => p.Skin).ToList();
                Assert.AreEqual(4, skins.Count);
                CollectionAssert.AreEquivalent(BishopVariantsRule.Skins, skins);
                firstSkins.Add(board.PieceAt(C("c1")).Skin);

                for (int i = 0; i < 6; i++) rule.OnTurnEnded(ctx, i % 2 == 0 ? Side.White : Side.Black);
                CollectionAssert.AreEqual(skins, board.AllPieces.Where(p => p.Type == PieceType.Bishop).Select(p => p.Skin).ToList());
            }
            Assert.Greater(firstSkins.Count, 1, "the looks should be assigned at random");
        }

        /// <summary>只留下两个王，再按 "Pa3" 这样的写法摆子（大写白方，小写黑方）。</summary>
        static Board Setup(params string[] pieces)
        {
            var board = Board.CreateStandard();
            foreach (var p in board.AllPieces.ToList())
                if (p.Type != PieceType.King) board.RemovePiece(p);
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
            return board;
        }

        static void Spray(Board board, string hydrant, int seed = 1, List<GameEvent> events = null)
        {
            var rook = board.PieceAt(C(hydrant));
            rook.Skin = "hydrant";
            new FireHydrantRule().OnPieceMoved(Context(board, seed, events), rook, rook.Pos);
        }

        [Test]
        public void Hydrant_PushesTheEnemyNextToItTwoSquaresAway_NeverItsOwnPieces()
        {
            var board = Setup("Rd4", "pd5", "Pc4", "Pd3");
            var pawn = board.PieceAt(C("d5"));
            var events = new List<GameEvent>();
            Spray(board, "d4", 1, events);
            Assert.AreEqual(C("d7"), pawn.Pos, "the enemy pawn is pushed straight up two squares");
            Assert.IsNotNull(board.PieceAt(C("c4")), "own pieces next to the hydrant stay put");
            Assert.IsNotNull(board.PieceAt(C("d3")));
            Assert.IsNull(board.PieceAt(C("d5")));
            var push = events.Single(e => e.Type == GameEventType.Pushed);
            Assert.AreEqual(C("d5"), push.From);
            Assert.AreEqual(C("d7"), push.To);
        }

        [Test]
        public void Hydrant_PrefersOrthogonalNeighbours_ThenDiagonal()
        {
            var board = Setup("Rd4", "pe5", "nd3");
            Spray(board, "d4");
            Assert.AreEqual(C("d1"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Knight).Pos, "the knight below is closer than the diagonal pawn");
            Assert.AreEqual(C("e5"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Pawn).Pos);

            board = Setup("Rd4", "pe5");
            Spray(board, "d4");
            Assert.AreEqual(C("g7"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Pawn).Pos, "diagonal push goes on along the diagonal");
        }

        [Test]
        public void Hydrant_StopsAtPiecesAndTheEdge_AndSkipsWhatCannotMove()
        {
            // d5 后面一格就有子：推不动，换另一枚
            var board = Setup("Rd4", "pd5", "pd6", "nc4");
            Spray(board, "d4");
            Assert.AreEqual(C("d5"), board.AllPieces.First(p => p.OnBoard && p.StartPos == C("d5")).Pos);
            Assert.AreEqual(C("a4"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Knight).Pos);

            // 第二格被挡住：只推一格
            board = Setup("Rd4", "pd5", "Pd7");
            Spray(board, "d4");
            Assert.AreEqual(C("d6"), board.AllPieces.First(p => p.OnBoard && p.StartPos == C("d5")).Pos);

            // 贴着棋盘边：推不出去
            board = Setup("Ra2", "na1");
            Spray(board, "a2");
            Assert.AreEqual(C("a1"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Knight).Pos);
        }

        [Test]
        public void Hydrant_PushesTheEnemyKing_AndPicksRandomlyAmongTheClosest()
        {
            var board = Setup("Rd8");
            Spray(board, "d8");
            Assert.AreEqual(C("g8"), board.King(Side.Black).Pos, "the enemy king next to the hydrant gets pushed too");
            board = Setup("Rd1");
            Spray(board, "d1");
            Assert.AreEqual(C("e1"), board.King(Side.White).Pos, "its own king is left alone");

            var pushed = new HashSet<string>();
            for (int seed = 0; seed < 40; seed++)
            {
                board = Setup("Rd4", "pd5", "pc4", "pe4", "pd3");
                // d3 的兵被推到第 1 行会升变成后，所以按“黑方的非王棋子”找
                var before = board.AllPieces.Where(p => p.OnBoard && p.Side == Side.Black && p.Type != PieceType.King).ToDictionary(p => p.Id, p => p.Pos);
                Spray(board, "d4", seed);
                var moved = board.AllPieces.Where(p => before.ContainsKey(p.Id) && p.Pos != before[p.Id]).ToList();
                Assert.AreEqual(1, moved.Count, "only one piece is pushed");
                pushed.Add(before[moved[0].Id].ToString());
            }
            Assert.Greater(pushed.Count, 1, "ties are broken at random");
        }

        [Test]
        public void Hydrant_PawnPushedOntoTheLastRankPromotes_AndOnlyRooksSpray()
        {
            var board = Setup("Rd4", "pd3");
            Spray(board, "d4");
            var pawn = board.AllPieces.First(p => p.OnBoard && p.StartPos == C("d3"));
            Assert.AreEqual(C("d1"), pawn.Pos, "a black pawn pushed onto rank 1 promotes");
            Assert.AreEqual(PieceType.Queen, pawn.Type);

            board = Setup("Nd4", "pd5");
            var knight = board.PieceAt(C("d4"));
            new FireHydrantRule().OnPieceMoved(Context(board, 1), knight, knight.Pos);
            Assert.AreEqual(C("d5"), board.AllPieces.First(p => p.OnBoard && p.Type == PieceType.Pawn).Pos);
        }

        [Test]
        public void CapturedPieces_StayMarkedAsLost_EvenAfterRevival()
        {
            var board = Board.CreateStandard();
            var knight = board.PieceAt(C("b1"));
            Assert.IsFalse(knight.WasLost);
            board.RemovePiece(knight);
            Assert.IsTrue(knight.WasLost);
            board.RestorePiece(knight, C("b1"));
            Assert.IsTrue(knight.OnBoard);
            Assert.IsTrue(knight.WasLost, "a revived piece keeps its paper look");
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

        /// <summary>跑 Crayon Comeback：挑 pieceSquare 原来那枚棋子（null = 跳过），放到 to（null = 跳过）。</summary>
        static (TargetRequest pick, TargetRequest place) Comeback(Board board, Piece piece, string to, List<GameEvent> events = null)
        {
            var requests = new CrayonRevivalRule().Activate(Context(board, 1, events)).GetEnumerator();
            Assert.IsTrue(requests.MoveNext());
            var pick = requests.Current;
            pick.PieceResult = piece?.Id;
            if (!requests.MoveNext()) return (pick, null);
            var place = requests.Current;
            place.Result = to == null ? (Coord?)null : C(to);
            Assert.IsFalse(requests.MoveNext());
            return (pick, place);
        }

        [Test]
        public void CrayonComeback_PlayerPicksAnyLostPiece_ThenASquareOnTheirHalf()
        {
            var board = Board.CreateStandard();
            Capture(board, "b1", "e2", "d1");
            var queen = board.AllPieces.Single(p => p.Type == PieceType.Queen && p.Side == Side.White);
            var (pick, place) = Comeback(board, queen, "d4");

            Assert.IsTrue(pick.IsPieceRequest);
            Assert.IsTrue(pick.AllowSkip);
            Assert.AreEqual(3, pick.PieceOptions.Count, "any lost piece can be picked, even the queen");
            Assert.IsFalse(place.IsPieceRequest);
            Assert.IsTrue(place.AllowSkip);
            Assert.AreEqual(queen.Id, place.PreviewPieceId);
            Assert.IsTrue(place.Options.All(c => c.Y <= 3 && board.IsEmpty(c) || c == C("d4")), "only free squares on the player's half");
            CollectionAssert.DoesNotContain(place.Options, C("d5"));
            Assert.IsTrue(queen.OnBoard);
            Assert.AreEqual(C("d4"), queen.Pos);
            Assert.AreEqual("crayon", queen.Skin, "queens have crayon art too");
        }

        [Test]
        public void CrayonComeback_LooksLikeCrayonWhenThereIsArt_AndKeepsOtherLooks()
        {
            var board = Board.CreateStandard();
            Capture(board, "b1", "h1");
            var knight = board.AllPieces.First(p => p.StartPos == C("b1"));
            var rook = board.AllPieces.First(p => p.StartPos == C("h1"));
            rook.Skin = "frog";

            var (_, place) = Comeback(board, knight, "c3");
            Assert.AreEqual("crayon", place.PreviewSkin);
            Assert.AreEqual("crayon", knight.Skin);

            Capture(board, "c1");
            var bishop = board.AllPieces.First(p => p.StartPos == C("c1"));
            (_, place) = Comeback(board, bishop, "d3");
            Assert.IsNull(bishop.Skin, "no crayon bishop yet: it comes back as paper");

            (_, place) = Comeback(board, rook, "h3");
            Assert.AreEqual("frog", place.PreviewSkin);
            Assert.AreEqual("frog", rook.Skin, "a frog comes back as a frog (it has a 3D model)");
        }

        [Test]
        public void CrayonComeback_PawnsAvoidTheBackRank_AndBothStepsCanBeSkipped()
        {
            var board = Board.CreateStandard();
            Assert.IsFalse(new CrayonRevivalRule().IsAvailable(Context(board, 1)), "nothing lost, nothing to bring back");
            Capture(board, "a2");
            var pawn = board.AllPieces.First(p => p.StartPos == C("a2"));
            var squares = RevivalPlacement.Squares(board, pawn);
            Assert.IsTrue(squares.All(c => c.Y >= 1 && c.Y <= 3));
            CollectionAssert.Contains(squares, C("a2"));

            var (_, place) = Comeback(board, null, null);
            Assert.IsNull(place, "skipping the piece ends the skill");
            Assert.IsFalse(pawn.OnBoard);

            (_, place) = Comeback(board, pawn, null);
            Assert.IsNotNull(place);
            Assert.IsFalse(pawn.OnBoard, "skipping the square ends the skill too");
            Assert.IsNull(pawn.Skin);
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
