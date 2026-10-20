using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class GameFlowTests
    {
        /// <summary>
        /// 双方都用 AI 走棋。固定选卡按 wanted[阶段] 里的 Id 选（选不到就选第一张）；
        /// 救急技能和第三阶段技能随机选或跳过；点选目标随机。
        /// </summary>
        public sealed class AutoPlayer
        {
            public readonly ChessGame Game;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            readonly ChessAI ai;
            readonly Random pick;
            readonly string[] wanted;

            public AutoPlayer(int seed, string[] wanted, int maxFullMoves = 120, GameSettings settings = null)
            {
                settings = settings ?? new GameSettings();
                settings.Seed = seed;
                settings.MaxFullMoves = maxFullMoves;
                Game = new ChessGame(settings);
                ai = new ChessAI(new Random(seed * 31 + 7));
                pick = new Random(seed * 17 + 3);
                this.wanted = wanted;
                Events.AddRange(Game.DrainEvents());
            }

            public void Step()
            {
                switch (Game.Phase)
                {
                    case GamePhase.ChoosingRule:
                        if (Game.CurrentOfferKind == OfferKind.StageCard)
                        {
                            int stage = Game.Offers[0].Stage;
                            int index = wanted == null ? 0 : Math.Max(0, Game.Offers.ToList().FindIndex(r => r.Id == wanted[stage]));
                            Game.ChooseRule(index);
                        }
                        else if (pick.Next(3) == 0) Game.SkipOffers();
                        else Game.ChooseRule(pick.Next(Game.Offers.Count));
                        break;
                    case GamePhase.ChoosingTarget:
                    {
                        var request = Game.PendingTarget;
                        if (request.IsPieceRequest)
                        {
                            if (request.AllowSkip && pick.Next(4) == 0) Game.ResolvePiece(null);
                            else Game.ResolvePiece(request.PieceOptions[pick.Next(request.PieceOptions.Count)]);
                        }
                        else if (request.AllowSkip && pick.Next(4) == 0) Game.ResolveTarget(null);
                        else Game.ResolveTarget(request.Options[pick.Next(request.Options.Count)]);
                        break;
                    }
                    case GamePhase.PlayerTurn:
                    case GamePhase.OpponentTurn:
                    {
                        var move = ai.ChooseMove(Game.Board);
                        Assert.IsTrue(move.HasValue, "BeginTurn should never leave a side to move without moves");
                        Game.PlayMove(move.Value);
                        break;
                    }
                }
                Events.AddRange(Game.DrainEvents());
            }

            public void PlayToEnd()
            {
                int guard = 0;
                while (Game.Phase != GamePhase.GameOver)
                {
                    Step();
                    Invariants.Check(Game);
                    Assert.Less(++guard, 5000, "game did not terminate");
                }
            }
        }

        static Coord C(string square) => new Coord(square[0] - 'a', square[1] - '1');

        /// <summary>把棋盘清到只剩指定的棋子（其余棋子当作被吃掉）。</summary>
        static void KeepOnly(Board board, params string[] squares)
        {
            foreach (var p in board.AllPieces.ToList())
                if (p.OnBoard && !squares.Contains(p.Pos.ToString())) board.RemovePiece(p);
        }

        static void Move(ChessGame game, string from, string to)
        {
            var piece = game.Board.PieceAt(C(from));
            game.PlayMove(new Move(piece.Id, C(from), C(to)));
        }

        [Test]
        public void TheOnlyStageCardIsOnTurn10_BeforePlayerMoves()
        {
            int seenGames = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var auto = new AutoPlayer(seed, null);
                int seen = 0;
                while (auto.Game.Phase != GamePhase.GameOver && !auto.Game.InStage3)
                {
                    if (auto.Game.Phase == GamePhase.ChoosingRule && auto.Game.CurrentOfferKind == OfferKind.StageCard)
                    {
                        Assert.LessOrEqual(auto.Game.Offers.Count, 3);
                        Assert.IsTrue(auto.Game.Offers.All(r => r.Stage == 1));
                        Assert.AreEqual(10, auto.Game.Board.FullMove);
                        Assert.AreEqual(Side.White, auto.Game.Board.SideToMove);
                        seen++;
                    }
                    auto.Step();
                }
                Assert.LessOrEqual(seen, 1);
                if (seen == 1) seenGames++;
            }
            Assert.Greater(seenGames, 0, "at least one game should reach the turn-10 card");
        }

        [Test]
        public void OpeningRulesTakeEffectOnTheirTurns_WithoutCards()
        {
            int reachedTurn7 = 0;
            for (int seed = 0; seed < 10; seed++)
            {
                var auto = new AutoPlayer(seed, null);
                var game = auto.Game;
                Assert.AreEqual(-1, game.Settings.OpeningRules.ToList().FindIndex(r => r.Turn < 4), "opening rules start after turn 4");
                while (!game.InStage3 && game.Phase != GamePhase.GameOver && game.Board.FullMove < 10)
                {
                    Assert.IsFalse(game.Phase == GamePhase.ChoosingRule && game.CurrentOfferKind == OfferKind.StageCard, "no cards before turn 10");
                    var ids = game.ActiveRules.Select(r => r.Id).ToList();
                    int turn = game.Board.FullMove;
                    if (turn < 5) Assert.IsFalse(ids.Contains("bishop_variants"), $"bishops changed on turn {turn}");
                    if (turn < 7) Assert.IsFalse(ids.Contains("fire_hydrant"), $"hydrants on turn {turn}");
                    if (turn >= 7 && game.Phase == GamePhase.PlayerTurn)
                    {
                        Assert.IsTrue(ids.Contains("bishop_variants") && ids.Contains("fire_hydrant"));
                        reachedTurn7++;
                        break;
                    }
                    auto.Step();
                }
                Assert.IsTrue(auto.Events.Any(e => e.Type == GameEventType.StageStarted && e.Stage == 0) || game.Board.FullMove < 4);
            }
            Assert.Greater(reachedTurn7, 0);
        }

        [Test]
        public void StormStartsByTurn20_AndOnlyRescueWins()
        {
            int stormed = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var auto = new AutoPlayer(seed, null);
                while (auto.Game.Phase != GamePhase.GameOver && !auto.Game.InStage3) auto.Step();
                if (auto.Game.Phase == GamePhase.GameOver)
                {
                    Assert.AreNotEqual(Side.White, auto.Game.Winner, "before stage 3 the player cannot win");
                    continue;
                }
                stormed++;
                Assert.LessOrEqual(auto.Game.Stage3StartTurn, 20);
                Assert.IsTrue(auto.Events.Any(e => e.Type == GameEventType.Storm));
                Assert.IsTrue(auto.Events.Any(e => e.Type == GameEventType.BoardRebuilt));
            }
            Assert.Greater(stormed, 0);
        }

        [Test]
        public void StormTurn_StartsStage3WithSkillOffer()
        {
            var game = new ChessGame(new GameSettings { Seed = 1, StageTurns = new int[0], StormTurn = 2 });
            Move(game, "e2", "e4");
            Move(game, "e7", "e5");
            Assert.IsTrue(game.InStage3);
            Assert.AreEqual(GamePhase.ChoosingRule, game.Phase);
            Assert.AreEqual(OfferKind.Stage3, game.CurrentOfferKind);
            Assert.IsTrue(game.CanSkipOffers);
            game.SkipOffers();
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase);
        }

        [Test]
        public void CheckingTheFriendAfterTheTurn10Card_StartsTheStorm()
        {
            var game = new ChessGame(new GameSettings { Seed = 1, StageTurns = new int[0] });
            KeepOnly(game.Board, "e1", "e8", "a1");
            Move(game, "a1", "a8"); // 将军
            Assert.IsTrue(game.InStage3);
            Assert.AreNotEqual(GamePhase.GameOver, game.Phase);
        }

        [Test]
        public void CheckingTheFriendBeforeTheTurn10Card_IsJustAMove()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            KeepOnly(game.Board, "e1", "e8", "a1");
            Move(game, "a1", "a8");
            Assert.IsFalse(game.InStage3);
        }

        [Test]
        public void CheckmatingTheFriend_StartsTheStormInsteadOfWinning()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            KeepOnly(game.Board, "e1", "e8", "g7", "h7", "a1");
            game.Board.Relocate(game.Board.King(Side.Black), C("h8"));
            Move(game, "a1", "a8"); // 底线将死
            Assert.IsTrue(game.InStage3);
            Assert.AreNotEqual(GamePhase.GameOver, game.Phase);
            Assert.IsNull(game.Winner);
        }

        [Test]
        public void GettingCheckmatedBeforeStage3_LosesTheGame_AfterTheEmergencySkill()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            KeepOnly(game.Board, "e1", "e8", "g2", "h2", "c2", "a8");
            game.Board.Relocate(game.Board.King(Side.White), C("h1"));
            Move(game, "c2", "c3");
            Move(game, "a8", "a1"); // 底线将死
            Assert.AreEqual(GamePhase.ChoosingRule, game.Phase);
            Assert.AreEqual(OfferKind.Emergency, game.CurrentOfferKind, "a last chance before the game ends");
            game.SkipOffers();
            Assert.AreEqual(GamePhase.GameOver, game.Phase);
            Assert.AreEqual(Side.Black, game.Winner);
            Assert.IsNotNull(game.Board.King(Side.White), "the king is never captured");
        }

        [Test]
        public void BeingInCheck_OffersAnEmergencySkill_OncePerTurn()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            KeepOnly(game.Board, "e1", "e8", "b1", "h2", "d8");
            game.DrainEvents();
            Move(game, "h2", "h3");
            Move(game, "d8", "d2"); // 后将军白王
            Assert.AreEqual(GamePhase.ChoosingRule, game.Phase);
            Assert.AreEqual(OfferKind.Emergency, game.CurrentOfferKind);
            Assert.IsTrue(game.DrainEvents().Any(e => e.Type == GameEventType.Check && e.Side == Side.White));
            game.SkipOffers();
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase, "skipping must not offer again in the same turn");
        }

        [Test]
        public void IllegalActionsAreRejected()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase);
            Assert.Throws<InvalidOperationException>(() => game.ChooseRule(0));
            Assert.Throws<InvalidOperationException>(() => game.SkipOffers());
            Assert.Throws<InvalidOperationException>(() => game.ResolveTarget(null));
            Assert.Throws<InvalidOperationException>(() => game.ResolvePiece(null));
            var blackPawn = game.Board.PieceAt(new Coord(0, 6));
            Assert.Throws<InvalidOperationException>(() =>
                game.PlayMove(new Move(blackPawn.Id, blackPawn.Pos, new Coord(0, 5))));
            Assert.AreEqual(0, game.MovesFor(blackPawn).Count);
        }

        [Test]
        public void StageCardsCannotBeSkipped()
        {
            var auto = new AutoPlayer(7, null);
            while (!(auto.Game.Phase == GamePhase.ChoosingRule && auto.Game.CurrentOfferKind == OfferKind.StageCard)) auto.Step();
            Assert.IsFalse(auto.Game.CanSkipOffers);
            Assert.Throws<InvalidOperationException>(() => auto.Game.SkipOffers());
        }

        [Test]
        public void RuleActivationEmitsEventsInOrder()
        {
            AutoPlayer auto = null;
            for (int seed = 7; auto == null; seed++)
            {
                var candidate = new AutoPlayer(seed, new[] { null, "tree_pawns" });
                while (candidate.Game.NextStage < 2 && candidate.Game.Phase != GamePhase.GameOver && !candidate.Game.InStage3)
                    candidate.Step();
                if (candidate.Game.Phase == GamePhase.ChoosingRule && candidate.Game.CurrentOfferKind == OfferKind.StageCard) auto = candidate;
            }
            Assert.AreEqual(GamePhase.ChoosingRule, auto.Game.Phase);
            Assert.AreEqual(GameEventType.RulesOffered, auto.Events.Last().Type);
            auto.Events.Clear();

            auto.Step();
            Assert.AreEqual(GameEventType.RuleActivated, auto.Events[0].Type);
            Assert.AreEqual("tree_pawns", auto.Events[0].Rule.Id);
            Assert.IsTrue(auto.Events.Any(e => e.Type == GameEventType.AppearanceChanged));
            Assert.Contains(auto.Game.ActiveRules.Last(), auto.Game.ActiveRules.ToList());
        }

        /// <summary>
        /// 第一阶段的自动规则 + 第 10 回合的每种选卡 × 多个种子，AI 对 AI 一直下到第三阶段结束（或回合上限），每一步都检查不变量。
        /// </summary>
        [Test]
        public void AllRuleCombinations_PlayThroughStage3WithoutBreakingInvariants()
        {
            var pools = Enumerable.Range(0, RuleCatalog.StageCount).Select(s => RuleCatalog.CreatePool(s).Select(r => r.Id).ToArray()).ToArray();
            var activated = new Dictionary<string, int>();
            int games = 0, stage3 = 0, sacrificeTasks = 0, knockbacks = 0, rescues = 0, emergencies = 0, friendSkills = 0, pushes = 0, undying = 0;
            const string a = "auto";

            foreach (var b in pools[1])
            {
                for (int seed = 0; seed < 12; seed++)
                {
                    var auto = new AutoPlayer(seed * 1000 + games, new[] { null, b });
                    try
                    {
                        auto.PlayToEnd();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Combination {a}/{b} seed {seed * 1000 + games} failed: {e.Message}", e);
                    }

                    foreach (var e in auto.Events)
                    {
                        if (e.Type == GameEventType.RuleActivated)
                            activated[e.Rule.Id] = activated.TryGetValue(e.Rule.Id, out var n) ? n + 1 : 1;
                        if (e.Type == GameEventType.Captured && e.Text == "burned")
                            CollectionAssert.Contains(Revivable, auto.Game.Board.GetPiece(e.PieceId).Type);
                        if (e.Type == GameEventType.KnockedBack) knockbacks++;
                        if (e.Type == GameEventType.Pushed) pushes++;
                        if (e.Type == GameEventType.OpponentSkill) friendSkills++;
                        if (e.Type == GameEventType.RulesOffered && e.OfferKind == OfferKind.Emergency) emergencies++;
                    }
                    if (auto.Game.InStage3) stage3++;
                    if (auto.Game.Board.TaskComplete) sacrificeTasks++;
                    if (auto.Game.Winner == Side.White) rescues++;
                    undying += auto.Game.Board.AllPieces.Count(p => p.Undying);
                    games++;
                }
            }

            TestContext.WriteLine($"{games} games, {stage3} reached stage 3, {sacrificeTasks} finished the lava task, " +
                                  $"{rescues} rescued the princess, {knockbacks} knockbacks, {pushes} hydrant pushes, {undying} undying pieces, {emergencies} emergency offers, {friendSkills} friend skills");
            foreach (var kv in activated.OrderBy(kv => kv.Key)) TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            foreach (var id in pools.SelectMany(p => p).Concat(new GameSettings().OpeningRules.Select(r => r.RuleId)))
                Assert.IsTrue(activated.ContainsKey(id), $"rule {id} was never activated");
            Assert.Greater(pushes, 0, "hydrants should push pieces around");
            Assert.Greater(stage3, games / 2, "most games should reach stage 3");
            Assert.Greater(sacrificeTasks, 0, "someone should finish the lava task");
            Assert.Greater(undying, 0, "sacrifices should make pieces undying");
            Assert.Greater(rescues, games / 2, "most games should end with a rescue");
            Assert.Greater(knockbacks, 0, "the friend should knock pieces back");
            Assert.Greater(emergencies, 0, "being in check should offer emergency skills");
            Assert.Greater(friendSkills, 0, "the friend should cast its scheduled skills");
        }

        /// <summary>龙火只烧这几种棋子。</summary>
        static readonly PieceType[] Revivable = { PieceType.Pawn, PieceType.Knight, PieceType.Bishop };
    }

    /// <summary>每一步之后都必须成立的条件。</summary>
    public static class Invariants
    {
        public static void Check(ChessGame game)
        {
            var board = game.Board;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                var p = board.PieceAt(c);
                if (p == null) continue;
                Assert.IsTrue(p.OnBoard, $"{p} on {c} should be on the board");
                Assert.AreEqual(c, p.Pos, $"{p} square mismatch");
                if (board.IsLava(c))
                    Assert.IsTrue(p.Side == game.PlayerSide && (board.TaskComplete || p.Undying),
                        $"{p} is standing on lava before the lava task is done");
                if (board.IsCastle(c)) Assert.AreEqual(GamePhase.GameOver, game.Phase, $"{p} is in the castle but the game goes on");
            }

            foreach (var p in board.AllPieces)
            {
                Assert.That(p.FrozenTurns, Is.InRange(0, 4), $"{p} frozen turns");
                if (p.Type == PieceType.King) Assert.IsFalse(p.IsFrozen, "kings are never frozen");
                if (p.Sacrificed || p.Retired) Assert.IsFalse(p.OnBoard, $"{p} came back after leaving for good");
                if (p.Undying)
                {
                    Assert.AreNotEqual(PieceType.King, p.Type);
                    Assert.AreEqual(game.PlayerSide, p.Side);
                    Assert.IsTrue(board.Stage3, "undying pieces only come from lava sacrifices");
                }
                if (p.OnBoard) Assert.AreSame(p, board.PieceAt(p.Pos), $"{p} missing from its square");
            }

            if (game.Phase != GamePhase.GameOver)
            {
                Assert.IsNotNull(board.King(game.PlayerSide), "the player's king is always on the board");
                if (!game.InStage3) Assert.IsNotNull(board.King(game.OpponentSide));
            }
            if (game.Phase == GamePhase.ChoosingRule) Assert.Greater(game.Offers.Count, 0);
            if (game.Phase == GamePhase.ChoosingTarget)
            {
                var r = game.PendingTarget;
                Assert.Greater(r.IsPieceRequest ? r.PieceOptions.Count : r.Options.Count, 0);
            }
            if (game.Phase == GamePhase.PlayerTurn || game.Phase == GamePhase.OpponentTurn)
                Assert.Greater(game.LegalMoves().Count, 0);
        }
    }
}
