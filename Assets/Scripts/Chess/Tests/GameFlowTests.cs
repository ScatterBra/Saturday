using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SaturdayGame.Chess.Tests
{
    public class GameFlowTests
    {
        /// <summary>双方都用 AI 走棋，规则按 wanted 里的 Id 选（选不到就选第一张），点选目标随机。</summary>
        sealed class AutoPlayer
        {
            public readonly ChessGame Game;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            readonly ChessAI ai;
            readonly Random pick;
            readonly string[] wanted;

            public AutoPlayer(int seed, string[] wanted, int maxFullMoves = 120)
            {
                Game = new ChessGame(new GameSettings { Seed = seed, MaxFullMoves = maxFullMoves });
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
                    {
                        int stage = Game.Offers[0].Stage;
                        int index = wanted == null ? 0 : Math.Max(0, Game.Offers.ToList().FindIndex(r => r.Id == wanted[stage]));
                        Game.ChooseRule(index);
                        break;
                    }
                    case GamePhase.ChoosingTarget:
                    {
                        var request = Game.PendingTarget;
                        if (request.AllowSkip && pick.Next(4) == 0) Game.ResolveTarget(null);
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

        /// <summary>龙火只烧、复活只复活这几种棋子。</summary>
        static readonly PieceType[] Revivable = { PieceType.Pawn, PieceType.Knight, PieceType.Bishop };

        [Test]
        public void StagesTriggerOnTurns4_10_16_BeforePlayerMoves()
        {
            int[] expectedTurns = { 4, 10, 16 };
            int fullGames = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var auto = new AutoPlayer(seed, null);
                int seen = 0;
                while (auto.Game.Phase != GamePhase.GameOver && seen < 3)
                {
                    if (auto.Game.Phase == GamePhase.ChoosingRule)
                    {
                        int stage = auto.Game.Offers[0].Stage;
                        Assert.LessOrEqual(auto.Game.Offers.Count, 3);
                        Assert.IsTrue(auto.Game.Offers.All(r => r.Stage == stage));
                        Assert.AreEqual(expectedTurns[stage], auto.Game.Board.FullMove);
                        Assert.AreEqual(Side.White, auto.Game.Board.SideToMove);
                        seen++;
                    }
                    auto.Step();
                }
                if (seen == 3) fullGames++;
            }
            Assert.Greater(fullGames, 0, "at least one game should reach all three stages");
        }

        [Test]
        public void IllegalActionsAreRejected()
        {
            var game = new ChessGame(new GameSettings { Seed = 1 });
            Assert.AreEqual(GamePhase.PlayerTurn, game.Phase);
            Assert.Throws<InvalidOperationException>(() => game.ChooseRule(0));
            Assert.Throws<InvalidOperationException>(() => game.ResolveTarget(null));
            var blackPawn = game.Board.PieceAt(new Coord(0, 6));
            Assert.Throws<InvalidOperationException>(() =>
                game.PlayMove(new Move(blackPawn.Id, blackPawn.Pos, new Coord(0, 5))));
            Assert.AreEqual(0, game.MovesFor(blackPawn).Count);
        }

        [Test]
        public void RuleActivationEmitsEventsInOrder()
        {
            var auto = new AutoPlayer(7, new[] { "fire_hydrant", "tree_pawns", "wizard_birds" });
            while (auto.Game.NextStage == 0) auto.Step();
            Assert.AreEqual(GamePhase.ChoosingRule, auto.Game.Phase);
            var offered = auto.Events.Last();
            Assert.AreEqual(GameEventType.RulesOffered, offered.Type);
            auto.Events.Clear();

            auto.Step();
            Assert.AreEqual(GameEventType.RuleActivated, auto.Events[0].Type);
            Assert.AreEqual("fire_hydrant", auto.Events[0].Rule.Id);
            Assert.IsTrue(auto.Events.Any(e => e.Type == GameEventType.AppearanceChanged));
            Assert.AreEqual(GamePhase.PlayerTurn, auto.Game.Phase);
            Assert.AreEqual(1, auto.Game.ActiveRules.Count);
        }

        /// <summary>
        /// 27 种规则组合 × 多个种子，AI 对 AI 打完整局，每一步都检查不变量。
        /// </summary>
        [Test]
        public void AllRuleCombinations_PlayToTheEndWithoutBreakingInvariants()
        {
            var pools = Enumerable.Range(0, 3).Select(s => RuleCatalog.CreatePool(s).Select(r => r.Id).ToArray()).ToArray();
            var activatedCount = new Dictionary<string, int>();
            int games = 0, stage3Reached = 0;

            foreach (var a in pools[0])
            foreach (var b in pools[1])
            foreach (var c in pools[2])
            {
                for (int seed = 0; seed < 3; seed++)
                {
                    var wanted = new[] { a, b, c };
                    var auto = new AutoPlayer(seed * 1000 + games, wanted);
                    try
                    {
                        auto.PlayToEnd();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Combination {a}/{b}/{c} seed {seed * 1000 + games} failed: {e.Message}", e);
                    }

                    foreach (var e in auto.Events)
                    {
                        if (e.Type == GameEventType.RuleActivated)
                            activatedCount[e.Rule.Id] = activatedCount.TryGetValue(e.Rule.Id, out var n) ? n + 1 : 1;
                        if (e.Type == GameEventType.Captured && e.Text == "burned")
                            CollectionAssert.Contains(Revivable, auto.Game.Board.GetPiece(e.PieceId).Type);
                        if (e.Type == GameEventType.Revived)
                            CollectionAssert.Contains(Revivable, auto.Game.Board.GetPiece(e.PieceId).Type);
                    }
                    if (auto.Game.ActiveRules.Any(r => r.Stage == 2)) stage3Reached++;
                    games++;
                }
            }

            TestContext.WriteLine($"{games} games, {stage3Reached} reached the end-game rules");
            foreach (var kv in activatedCount.OrderBy(kv => kv.Key)) TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            foreach (var pool in pools)
                foreach (var id in pool)
                    Assert.IsTrue(activatedCount.ContainsKey(id), $"rule {id} was never activated");
        }
    }

    /// <summary>每一步之后都必须成立的条件。</summary>
    static class Invariants
    {
        public static void Check(ChessGame game)
        {
            var board = game.Board;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                var p = board.PieceAt(c);
                if (p == null) continue;
                Assert.IsTrue(p.OnBoard && !p.IsHidden, $"{p} on {c} should be visible");
                Assert.AreEqual(c, p.Pos, $"{p} square mismatch");
                Assert.IsFalse(board.IsLava(c), $"{p} is standing on lava");
            }

            foreach (var p in board.AllPieces)
            {
                Assert.That(p.FrozenTurns, Is.InRange(0, 4), $"{p} frozen turns");
                if (p.Type == PieceType.King)
                {
                    Assert.IsFalse(p.IsFrozen, "kings are never frozen");
                    Assert.IsFalse(p.IsHidden, "kings never hide");
                    Assert.IsFalse(p.IsCastle);
                }
                if (p.IsCastle) Assert.AreEqual(game.PlayerSide, p.Side, "only the player gets castles");
                if (!p.OnBoard)
                {
                    Assert.IsFalse(p.IsHidden);
                    continue;
                }
                if (p.IsHidden)
                {
                    var host = board.GetPiece(p.HostId);
                    Assert.IsTrue(host.IsCastle && host.OnBoard && host.GuestId == p.Id);
                    Assert.AreEqual(host.Pos, p.Pos);
                }
                else
                {
                    Assert.AreSame(p, board.PieceAt(p.Pos), $"{p} missing from its square");
                }
            }

            int kings = board.AllPieces.Count(p => p.OnBoard && p.Type == PieceType.King);
            if (game.Phase != GamePhase.GameOver) Assert.AreEqual(2, kings);
            if (game.Phase == GamePhase.ChoosingRule) Assert.Greater(game.Offers.Count, 0);
            if (game.Phase == GamePhase.ChoosingTarget) Assert.Greater(game.PendingTarget.Options.Count, 0);
            if (game.Phase == GamePhase.PlayerTurn || game.Phase == GamePhase.OpponentTurn)
                Assert.Greater(game.LegalMoves().Count, 0);
        }
    }
}
