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

        /// <summary>城堡在 a8、a7、b8，其余是平地。</summary>
        const string CastleCorner =
            "CC......\nC.......\n........\n........\n........\n........\n........\n........";

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

        /// <summary>跑一个技能，每次点选都按 picks 的顺序回答（null = 跳过）；返回点选请求。</summary>
        static List<TargetRequest> Run(RuleBase rule, RuleContext ctx, params string[] picks)
        {
            var requests = new List<TargetRequest>();
            int next = 0;
            foreach (var request in rule.Activate(ctx))
            {
                requests.Add(request);
                Assert.Less(next, picks.Length, $"{rule.Id} asked for more targets than expected: {request.Prompt}");
                var pick = picks[next++];
                if (pick != null) CollectionAssert.Contains(request.Options, C(pick), $"{pick} should be a valid option");
                request.Result = pick != null ? C(pick) : (Coord?)null;
            }
            return requests;
        }

        static ChessGame Stage3Game(int seed, Action<GameSettings> tweak = null)
        {
            var settings = new GameSettings { Seed = seed, StageTurns = new int[0], StormTurn = 1, FriendSkillTurns = new int[0] };
            tweak?.Invoke(settings);
            var game = new ChessGame(settings);
            Assert.IsTrue(game.InStage3);
            return game;
        }

        // ---------- 地形和风暴 ----------

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
        public void TheStorm_SendsTheKingAndQueenIntoTheCastle_AndScattersEveryoneElse()
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
                var blackKing = board.King(Side.Black);
                var blackQueen = board.PieceAt(C("d8"));

                board.BeginStage3(Stage3Layout.Default, Side.White);
                board.Scatter(new Random(seed), 5);

                Assert.IsTrue(board.Stage3);
                Assert.AreEqual(Side.White, board.SideToMove);
                Assert.AreEqual(15, board.PiecesOf(Side.White).Count(), "nobody is left out");
                Assert.AreEqual(16, board.PiecesOf(Side.Black).Count(), "the friend keeps its whole army");
                Assert.IsTrue(board.IsCastle(blackKing.Pos) && board.InCastle(blackKing), "the king hides in the castle");
                Assert.IsTrue(board.IsCastle(blackQueen.Pos), "so does the queen");
                Assert.AreEqual(14, board.GuardCount);
                Assert.IsFalse(board.GateOpen);
                Assert.IsFalse(lostKnight.OnBoard, "captured pieces stay captured");
                Assert.AreEqual("frog", rook.Skin);
                Assert.AreEqual(2, pawn.FrozenTurns, "effects carry over");

                var lava = Enumerable.Range(0, 64).Select(Coord.FromIndex).Where(board.IsLava).ToList();
                Assert.AreEqual(5, lava.Count);
                var whiteKing = board.King(Side.White);
                foreach (var p in board.AllPieces.Where(p => p.OnBoard))
                {
                    Assert.IsFalse(board.IsLava(p.Pos), $"{p} landed on lava");
                    Assert.AreSame(p, board.PieceAt(p.Pos));
                    if (p != blackKing && p != blackQueen) Assert.IsFalse(board.IsCastle(p.Pos), $"{p} landed in the castle");
                    if (p.Type == PieceType.Pawn) Assert.That(p.Pos.Y, Is.InRange(1, 6), $"{p} on the first or last rank");
                    if (p.Side == Side.Black) Assert.GreaterOrEqual(p.Pos.Y, 4, $"{p} should guard the castle half");
                }
                Assert.IsFalse(board.PiecesOf(Side.Black).Any(o => o.Pos.DistanceTo(whiteKing.Pos) <= 1), "the player's king starts safe");
                foreach (var c in lava)
                {
                    Assert.Greater(c.DistanceTo(whiteKing.Pos), 1, "no lava right next to the player's king");
                    Assert.IsFalse(board.NextToCastle(c), "no lava right next to the castle");
                }
                Assert.GreaterOrEqual(lava.Count(c => board.PiecesOf(Side.Black).Any(p => !board.InCastle(p) && p.Pos.DistanceTo(c) <= 1)), 2,
                    "some lava lies next to the guards");
            }
        }

        [Test]
        public void ALostQueen_StillShowsUpInTheCastle()
        {
            var board = Board.CreateStandard();
            board.RemovePiece(board.PieceAt(C("d8")));
            board.BeginStage3(Stage3Layout.Default, Side.White);
            board.Scatter(new Random(1), 5);
            var queens = board.PiecesOf(Side.Black).Where(p => p.Type == PieceType.Queen).ToList();
            Assert.AreEqual(1, queens.Count);
            Assert.IsTrue(board.InCastle(queens[0]), "she escaped into the castle");
            Assert.AreEqual(14, board.GuardCount, "she isn't a guard");
        }

        [Test]
        public void TheStorm_InAGame_TalksAboutTheCastleAndTheLava_AndSetsUpTheThunderBar()
        {
            var game = Stage3Game(5);
            var events = game.DrainEvents();
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Dialogue && e.Text.Contains("castle")));
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Dialogue && e.Text.Contains("lava")));
            Assert.AreEqual(14, game.InitialGuards);
            Assert.AreEqual(12, game.ThunderMax, "full when only two guards are left");
            Assert.AreEqual(0, game.ThunderCharge);
            Assert.AreEqual(new GameSettings().LavaSquares, Enumerable.Range(0, 64).Count(i => game.Board.IsLava(Coord.FromIndex(i))));
        }

        // ---------- 走法 ----------

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

        [Test]
        public void BothSidesCaptureNormally_AndKingsCanBeTaken()
        {
            var board = Setup(Flat, new[] { "Ke1", "Nd4", "rd6", "re8", "kh8" });
            var knight = board.PieceAt(C("d4"));
            var friendMoves = MoveGenerator.Generate(board, Side.Black).Select(m => m.To).ToList();
            CollectionAssert.Contains(friendMoves, C("e1"), "no check rules: the king is just another piece");

            board.Pass();
            var result = board.Apply(new Move(board.PieceAt(C("d6")).Id, C("d6"), C("d4")));
            Assert.AreSame(knight, result.Captured);
            Assert.IsFalse(knight.OnBoard);
        }

        [Test]
        public void TheCastleIsShutUntilTheGuardsAreGone_ThenAnyPieceWalksIn()
        {
            var board = Setup(CastleCorner, new[] { "Ke1", "Rb1", "Pb6", "ka8", "qa7", "ph5" });
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, board.PieceAt(C("a8"))).Count, "the king stays in the castle");
            Assert.AreEqual(0, MoveGenerator.MovesFor(board, board.PieceAt(C("a7"))).Count, "so does the queen");
            CollectionAssert.DoesNotContain(Targets(board, "b1"), C("b8"), "the gate is shut");
            CollectionAssert.DoesNotContain(Targets(board, "b6"), C("a7"), "nobody touches the queen yet");
            Assert.IsFalse(board.GateOpen);

            board.RemovePiece(board.PieceAt(C("h5")));
            Assert.IsTrue(board.GateOpen, "no guards left");
            CollectionAssert.DoesNotContain(Targets(board, "b1"), C("b8"), "the pawn on b6 is in the rook's way");
            CollectionAssert.Contains(Targets(board, "b6"), C("a7"), "a pawn can take the queen in the castle");
            Assert.IsFalse(Play(board, "b6", "b7").CastleTaken);
            CollectionAssert.Contains(Targets(board, "b7"), C("b8"), "walking straight into the empty castle square");
            Assert.IsTrue(Play(board, "b7", "b8").CastleTaken);
        }

        [Test]
        public void SlidingPiecesStopWhenTheyEnterTheCastle()
        {
            var board = Setup("........\n........\n...C....\n...C....\n........\n........\n........\n........",
                new[] { "Ke1", "Rd1", "kd6" });
            Assert.IsTrue(board.GateOpen, "the only black piece is inside the castle");
            var rook = Targets(board, "d1");
            CollectionAssert.Contains(rook, C("d5"));
            CollectionAssert.DoesNotContain(rook, C("d6"), "the rook stops inside the castle");
        }

        // ---------- 潮湿和爆炸 ----------

        [Test]
        public void ADragonBurningAWetPiece_BlowsItsNeighboursAway_AndLeavesALavaPit()
        {
            // d4 是潮湿的兵；c3 被推到 b2，e5 推到 f6，d5 后面有车挡着推不动，e4 被推进 f4 的岩浆（也是潮湿的，但不连锁）
            var board = Setup(Flat, new[] { "Ke1", "Nb5", "Pc3", "Rd6", "pd4", "pe5", "pd5", "pe4", "pg4", "kh8" }, "f4");
            foreach (var s in new[] { "d4", "e4" }) board.PieceAt(C(s)).WetTurns = 3;
            var pushedIntoLava = board.PieceAt(C("e4"));
            var events = new List<GameEvent>();
            Run(new DragonKnightRule(), Context(board, 1, events), "d4");

            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Exploded && e.To == C("d4")));
            Assert.AreEqual(PieceType.Pawn, board.PieceAt(C("b2"))?.Type, "c3 is blasted to b2 (friendly fire)");
            Assert.AreEqual(Side.White, board.PieceAt(C("b2")).Side);
            Assert.IsNotNull(board.PieceAt(C("f6")), "e5 is blasted to f6");
            Assert.IsNotNull(board.PieceAt(C("d5")), "d5 is blocked by the rook, so it stays");
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.KnockedBack && e.From == C("d5") && e.To == C("d5")), "it still gets a bump");
            Assert.IsFalse(pushedIntoLava.OnBoard, "e4 is blasted into the lava");
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Captured && e.PieceId == pushedIntoLava.Id && e.Text == "lava"));
            Assert.AreEqual(1, events.Count(e => e.Type == GameEventType.Exploded), "no chain reaction");
            Assert.IsNotNull(board.PieceAt(C("g4")), "nothing happens around f4");
            Assert.IsTrue(board.IsLava(C("d4")), "a lava pit is left behind");
            Assert.IsNotNull(board.PieceAt(C("b5")), "the knight is out of the blast");
        }

        [Test]
        public void ADryPieceJustBurns()
        {
            var board = Setup(Flat, new[] { "Ke1", "Nb5", "Pc3", "pd4", "kh8" });
            var events = new List<GameEvent>();
            Run(new DragonKnightRule(), Context(board, 1, events), "d4");
            Assert.IsFalse(events.Any(e => e.Type == GameEventType.Exploded));
            Assert.IsNotNull(board.PieceAt(C("c3")));
            Assert.IsFalse(board.IsLava(C("d4")));
        }

        [Test]
        public void AWetPieceSprayedIntoLava_Explodes_ADryOneDoesNot()
        {
            var board = Setup(Flat, new[] { "Ke1", "Rd4", "pd5", "pe6", "kh8" }, "d6");
            board.PieceAt(C("d5")).WetTurns = 2;
            var rook = board.PieceAt(C("d4"));
            rook.Skin = "hydrant";
            var events = new List<GameEvent>();
            new FireHydrantRule().OnPieceMoved(Context(board, 1, events), rook, rook.Pos);
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Exploded && e.To == C("d6")));
            Assert.IsNotNull(board.PieceAt(C("f6")), "e6 is blasted away from the lava");

            board = Setup(Flat, new[] { "Ke1", "Rd4", "pd5", "pe6", "kh8" }, "d6");
            rook = board.PieceAt(C("d4"));
            rook.Skin = "hydrant";
            events.Clear();
            new FireHydrantRule().OnPieceMoved(Context(board, 1, events), rook, rook.Pos);
            Assert.IsFalse(events.Any(e => e.Type == GameEventType.Exploded));
            Assert.IsNotNull(board.PieceAt(C("e6")));
        }

        [Test]
        public void HydrantsAndFrogsLeaveTheCastleAlone_ButKingsCanFallIntoLava()
        {
            var board = Setup(CastleCorner, new[] { "Ke1", "Rb7", "ka8", "qa7", "ph5" });
            var hydrant = board.PieceAt(C("b7"));
            hydrant.Skin = "hydrant";
            Assert.AreEqual(0, FireHydrantRule.EffectCandidates(board, hydrant).Count, "the king and queen can't be sprayed out of the castle");

            board = Setup(Flat, new[] { "Ke1", "Rd4", "kd5" }, "d6");
            var spray = board.PieceAt(C("d4"));
            spray.Skin = "hydrant";
            new FireHydrantRule().OnPieceMoved(Context(board, 1), spray, spray.Pos);
            Assert.IsNull(board.King(Side.Black), "in stage 3 the king is an ordinary piece");
        }

        [Test]
        public void NoLavaPitNextToTheCastle()
        {
            var board = Setup(CastleCorner, new[] { "Ke1", "ka8", "qa7", "pc5" });
            Context(board, 1).Explode(C("b6"));
            Assert.IsFalse(board.IsLava(C("b6")), "b6 touches the castle");
            Assert.IsNotNull(board.PieceAt(C("a7")), "the queen isn't blasted out of the castle");
            Assert.IsNotNull(board.PieceAt(C("d4")), "c5 is blasted to d4");
            Context(board, 1).Explode(C("d5"));
            Assert.IsTrue(board.IsLava(C("d5")));
        }

        // ---------- 技能 ----------

        [Test]
        public void TheBeam_WipesOutTheWholeDiagonal_YoursToo_AndStopsAtTheCastle()
        {
            var board = Setup(Flat, new[] { "Ke1", "Bc1", "Pd2", "pe3", "pg5", "pb2", "kh8" }, "f4");
            var events = new List<GameEvent>();
            var ctx = Context(board, 1, events);
            var requests = Run(new BishopBeamSkill(), ctx, "e3");
            CollectionAssert.AreEquivalent(new[] { C("d2"), C("e3"), C("f4"), C("g5"), C("h6") }, requests[0].Affected(C("g5")),
                "the preview shows the whole line");
            Assert.IsTrue(ctx.Acted);
            Assert.IsNull(board.PieceAt(C("d2")), "friendly fire");
            Assert.IsNull(board.PieceAt(C("e3")));
            Assert.IsNull(board.PieceAt(C("g5")), "the beam flies over the lava");
            Assert.IsNotNull(board.PieceAt(C("b2")), "other diagonals are fine");
            Assert.IsNotNull(board.PieceAt(C("c1")), "the bishop stays put");
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Beam && e.From == C("c1") && e.To == C("h6")));

            board = Setup("........\n........\n........\n........\n.....C..\n........\n........\n........",
                new[] { "Ke1", "Bc1", "pe3", "kh8" });
            var path = BishopBeamSkill.Path(board, C("c1"), new Coord(1, 1));
            CollectionAssert.AreEqual(new[] { C("d2"), C("e3") }, path, "the beam stops in front of the castle");
        }

        [Test]
        public void TheBeam_SkippingDoesNothing_FrozenBishopsCantFire()
        {
            var board = Setup(Flat, new[] { "Ke1", "Bc1", "pe3", "kh8" });
            var ctx = Context(board, 1);
            Run(new BishopBeamSkill(), ctx, (string)null);
            Assert.IsFalse(ctx.Acted);
            Assert.IsNotNull(board.PieceAt(C("e3")));

            board.PieceAt(C("c1")).FrozenTurns = 2;
            Assert.IsFalse(new BishopBeamSkill().IsAvailable(Context(board, 1)));
        }

        [Test]
        public void Thunder_HitsA4x4Area_OnlyEnemies_NeverTheCastle()
        {
            CollectionAssert.AreEquivalent(
                new[] { "a1", "b1", "c1", "d1", "a2", "b2", "c2", "d2", "a3", "b3", "c3", "d3", "a4", "b4", "c4", "d4" }.Select(C),
                ThunderSkill.Area(C("a1")), "the area is pushed inside the board");
            Assert.AreEqual(16, ThunderSkill.Area(C("e5")).Distinct().Count());
            CollectionAssert.Contains(ThunderSkill.Area(C("e5")), C("d4"));
            CollectionAssert.Contains(ThunderSkill.Area(C("e5")), C("g7"));

            var board = Setup(CastleCorner, new[] { "Ke1", "Pb5", "ka8", "qa7", "pa6", "pc6", "pg2" });
            var ctx = Context(board, 1);
            Run(new ThunderSkill(), ctx, "b6");
            Assert.IsTrue(ctx.Acted);
            Assert.IsNull(board.PieceAt(C("a6")));
            Assert.IsNull(board.PieceAt(C("c6")));
            Assert.IsNotNull(board.PieceAt(C("b5")), "your own pieces are safe");
            Assert.IsNotNull(board.PieceAt(C("a7")), "the castle is safe");
            Assert.IsNotNull(board.PieceAt(C("g2")), "outside the area");
        }

        [Test]
        public void Rain_SoaksEveryoneButTheCastle_AndDriesAfterThreeTurns()
        {
            var board = Setup(CastleCorner, new[] { "Ke1", "Pb2", "ka8", "qa7", "ph5" });
            var events = new List<GameEvent>();
            Run(new RainRule(), Context(board, 1, events));
            Assert.IsTrue(new[] { "e1", "b2", "h5" }.All(s => board.PieceAt(C(s)).IsWet));
            Assert.IsFalse(board.PieceAt(C("a7")).IsWet);
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.Soaked && e.Squares.Count == 3));

            var pawn = board.PieceAt(C("b2"));
            for (int turn = 0; turn < RainRule.WetTurns; turn++)
            {
                Assert.IsTrue(pawn.IsWet, $"still wet after {turn} turns");
                board.Pass();
                board.Pass();
            }
            Assert.IsFalse(pawn.IsWet);
        }

        [Test]
        public void Landslide_SlidesEveryoneOffTheHighGround_IntoLavaIfThatsLower()
        {
            const string hill =
                "........\n........\n........\n........\n...2....\n...1....\n........\n........";
            for (int seed = 0; seed < 10; seed++)
            {
                var board = Setup(hill, new[] { "Ke1", "Pd4", "pd3", "kh8" }, "c4");
                var top = board.PieceAt(C("d4"));
                var low = board.PieceAt(C("d3"));
                var events = new List<GameEvent>();
                Assert.IsTrue(new LandslideRule().IsAvailable(Context(board, seed)));
                Run(new LandslideRule(), Context(board, seed, events));
                Assert.IsTrue(!top.OnBoard || board.Height(top.Pos) == 0, "the piece on top slides to the lowest square around it");
                Assert.IsTrue(!low.OnBoard || board.Height(low.Pos) == 0, "the piece on the slope slides off");
                Assert.AreEqual(2, events.Count(e => e.Type == GameEventType.Pushed));
            }

            // 一圈高地中间只有一格更低的岩浆：只能滑进去
            const string bowl =
                "11111111\n11111111\n11111111\n11111111\n11.11111\n11111111\n11111111\n11111111";
            var pit = Setup(bowl, new[] { "Ke1", "Pd4", "kh8" }, "c4");
            var pawn = pit.PieceAt(C("d4"));
            Run(new LandslideRule(), Context(pit, 1));
            Assert.IsFalse(pawn.OnBoard, "slid into the lava");
            Assert.AreEqual(C("e1"), pit.King(Side.White).Pos, "nothing lower around the king, it stays");
        }

        [Test]
        public void Landslide_IsUnavailableOnFlatGround()
        {
            var board = Setup(Flat, new[] { "Ke1", "Pd4", "kh8" });
            Assert.IsFalse(new LandslideRule().IsAvailable(Context(board, 1)));
        }

        [Test]
        public void LastStand_BlowsUpYourOwnPiece_AndChargesTheStorm()
        {
            var board = Setup(Flat, new[] { "Ke1", "Nd4", "pd5", "pc3", "kh8" });
            var ctx = Context(board, 1);
            var requests = Run(new SelfDestructRule(), ctx, "d4");
            CollectionAssert.DoesNotContain(requests[0].Options, C("e1"), "not the king");
            Assert.IsTrue(ctx.Acted);
            Assert.IsNull(board.PieceAt(C("d4")));
            Assert.IsTrue(board.IsLava(C("d4")), "a lava pit is left behind");
            Assert.IsNotNull(board.PieceAt(C("d6")), "d5 is blasted to d6");
            Assert.IsNotNull(board.PieceAt(C("b2")), "c3 is blasted to b2");
            Assert.AreEqual(SelfDestructRule.ThunderBonus, ctx.TakeThunderCharge());
        }

        // ---------- 一整局 ----------

        [Test]
        public void Stage3Cards_GoIntoYourHand_SkippingChargesTheStorm()
        {
            var game = Stage3Game(2);
            Assert.AreEqual(GamePhase.ChoosingRule, game.Phase);
            Assert.AreEqual(OfferKind.Stage3, game.CurrentOfferKind);
            Assert.AreEqual(2, game.Offers.Count);
            Assert.IsTrue(game.Offers.All(r => r.Id != "crayon_revival"), "no reviving in stage 3");
            var card = game.Offers[0];
            game.ChooseRule(0);
            Assert.AreSame(card, game.HandCard);
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase, "picking a card doesn't use your turn");

            game.BankHandCard();
            Assert.IsNull(game.HandCard);
            Assert.AreEqual(1, game.ThunderCharge);
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase);

            game = Stage3Game(2);
            game.SkipOffers();
            Assert.AreEqual(1, game.ThunderCharge, "skipping the cards charges the storm");
        }

        [Test]
        public void UsingAHandCard_DoesNotTakeYourTurn()
        {
            var game = Stage3Game(4);
            int rain = game.Offers.ToList().FindIndex(r => r.Id == "rain");
            for (int seed = 5; rain < 0; seed++)
            {
                game = Stage3Game(seed);
                rain = game.Offers.ToList().FindIndex(r => r.Id == "rain");
            }
            game.ChooseRule(rain);
            Assert.IsTrue(game.CanUseHandCard);
            game.UseHandCard();
            Assert.IsNull(game.HandCard);
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase);
            Assert.IsTrue(game.Board.PiecesOf(Side.White).All(p => p.IsWet));
        }

        [Test]
        public void TheBeamTakesYourTurn_AndCoolsDown_SkippingItDoesNot()
        {
            var game = Stage3Game(3);
            game.SkipOffers();
            int seed = 3;
            while (!game.CanUseSkill(Stage3Skill.Beam))
            {
                game = Stage3Game(++seed);
                game.SkipOffers();
            }
            int turn = game.Board.FullMove;

            game.UseSkill(Stage3Skill.Beam);
            while (game.Phase == GamePhase.ChoosingTarget && game.PendingTarget.Options.Count > 0 && game.PendingTarget.Affected == null)
                game.ResolveTarget(game.PendingTarget.Options[0]);
            game.ResolveTarget(null);
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase, "skipped: still your turn");
            Assert.AreEqual(0, game.Cooldown(Stage3Skill.Beam));

            game.UseSkill(Stage3Skill.Beam);
            while (game.Phase == GamePhase.ChoosingTarget) game.ResolveTarget(game.PendingTarget.Options[0]);
            Assert.AreNotEqual(Side.White, game.Board.SideToMove, "the beam used up the turn");
            Assert.Greater(game.Cooldown(Stage3Skill.Beam), 0);
            Assert.AreEqual(turn + game.Settings.BeamCooldown + 1 - game.Board.FullMove, game.Cooldown(Stage3Skill.Beam));
        }

        [Test]
        public void TheQueenWarnsAboutHerBirds_ThenOnlyHitsPiecesStillOnTheMarkedSquares()
        {
            var game = Stage3Game(3, s => s.FriendSkillTurns = new[] { 0 });
            game.SkipOffers();
            game.DrainEvents();
            game.PlayMove(game.LegalMoves().First());
            var events = game.DrainEvents();
            var warning = events.SingleOrDefault(e => e.Type == GameEventType.SkillTelegraphed);
            Assert.IsNotNull(warning, "the queen warns first");
            Assert.IsFalse(events.Any(e => e.Type == GameEventType.OpponentSkill), "and casts next turn");
            CollectionAssert.AreEquivalent(warning.Squares, game.FriendTelegraph);

            // 把一个被标记的棋子走开（如果能走），然后看鸟群只砸还在格子上的
            var marked = warning.Squares.Select(c => game.Board.PieceAt(c)).Where(p => p != null && p.Side == Side.White).ToList();
            if (game.Phase == GamePhase.OpponentTurn) game.PlayMove(game.LegalMoves().First());
            while (game.Phase == GamePhase.ChoosingRule) game.SkipOffers();
            var dodge = game.LegalMoves().FirstOrDefault(m => marked.Any(p => p.Id == m.PieceId) && !warning.Squares.Contains(m.To));
            var dodger = game.LegalMoves().Contains(dodge) ? game.Board.GetPiece(dodge.PieceId) : null;
            game.PlayMove(dodger != null ? dodge : game.LegalMoves().First());
            events = game.DrainEvents();
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.OpponentSkill && e.Rule.Id == "wizard_birds"));
            foreach (var p in game.Board.PiecesOf(Side.White).Where(p => p.FrozenSkin == "poop"))
                Assert.IsTrue(warning.Squares.Contains(p.Pos), $"{p} was hit outside the marked squares");
            if (dodger != null) Assert.IsFalse(dodger.IsFrozen, "the piece that moved away dodged the birds");
            Assert.IsTrue(game.Board.PiecesOf(Side.Black).All(p => !p.IsFrozen));
        }

        [Test]
        public void LosingEveryPiece_LosesTheGame()
        {
            var game = Stage3Game(1);
            foreach (var p in game.Board.PiecesOf(Side.White).ToList()) game.Board.RemovePiece(p);
            game.SkipOffers();
            Assert.AreEqual(GamePhase.GameOver, game.Phase);
            Assert.AreEqual(Side.Black, game.Winner);
        }

        [Test]
        public void ClearingTheGuards_OpensTheGate_AndWalkingInWins()
        {
            var game = Stage3Game(6);
            game.SkipOffers();
            game.DrainEvents();
            foreach (var p in game.Board.PiecesOf(Side.Black).Where(p => !game.Board.InCastle(p)).ToList()) game.Board.RemovePiece(p);
            Assert.AreEqual(game.ThunderMax, game.ThunderCharge, "every guard lost fills the bar");

            // 随便走一步，朋友只剩城堡里的国王、王后：跳过，城门打开
            game.PlayMove(game.LegalMoves().First());
            var events = game.DrainEvents();
            Assert.IsTrue(events.Any(e => e.Type == GameEventType.GateOpened));
            Assert.IsTrue(game.GateOpen);
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase, "the friend can only wait");

            var ai = new ChessAI(new Random(1)) { BlunderChance = 0 };
            for (int i = 0; i < 60 && game.Phase != GamePhase.GameOver; i++)
            {
                if (game.Phase == GamePhase.ChoosingRule) game.SkipOffers();
                else game.PlayMove(ai.ChooseMove(game.Board).Value);
            }
            Assert.AreEqual(Side.White, game.Winner, "the attacker walks into the castle");
        }

        [Test]
        public void Stage3Games_AlwaysEnd_AndKeepTheBoardConsistent()
        {
            int won = 0, lost = 0, explosions = 0, beams = 0, thunders = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var auto = new GameFlowTests.AutoPlayer(seed, null, 120,
                    new GameSettings { StageTurns = new int[0], StormTurn = 1 });
                auto.PlayToEnd();
                if (auto.Game.Winner == Side.White) won++;
                if (auto.Game.Winner == Side.Black) lost++;
                explosions += auto.Events.Count(e => e.Type == GameEventType.Exploded);
                beams += auto.Events.Count(e => e.Type == GameEventType.Beam);
                thunders += auto.Events.Count(e => e.Type == GameEventType.Thunder);
            }
            TestContext.WriteLine($"{won} won, {lost} lost, {explosions} explosions, {beams} beams, {thunders} thunderstorms");
            Assert.Greater(beams, 0, "the auto player should fire some beams");
            Assert.Greater(won + lost, 0, "some games should be decided");
        }
    }
}
