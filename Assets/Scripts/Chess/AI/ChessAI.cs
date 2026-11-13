using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 弱 AI：能将死就将死；偶尔随手乱走（犯错）；其余时候看两步——自己这一步之后，
    /// 对方最贪心的回应会吃掉什么，选子力最划算的走法，再加一点随机噪声。
    /// 走法都是走法生成器给的合法走法，所以它不会送将，也不会吃王。
    /// AI 不知道有哪些规则，它只调用走法生成器，所以自然会遵守冻结、城堡、岩浆等状态。
    /// 第三阶段双方正常吃子、不判将军：同样看两步，王也算子力（王被吃不会直接输）；
    /// 攻城的一方（测试里由 AI 代下）城门开了就往城堡走，能进就进。AI 不会放技能。
    /// </summary>
    public sealed class ChessAI
    {
        /// <summary>每一步有这么大的概率随手乱走。</summary>
        public double BlunderChance = 0.2;
        /// <summary>评分噪声幅度（单位：兵的价值）。</summary>
        public double Noise = 0.6;


        readonly Random rng;

        public ChessAI(Random rng)
        {
            this.rng = rng;
        }

        public Move? ChooseMove(Board board)
        {
            var side = board.SideToMove;
            var moves = MoveGenerator.Generate(board, side);
            if (moves.Count == 0) return null;
            if (board.Stage3) return ChooseStage3Move(board, side, moves);

            foreach (var move in moves)
            {
                var next = board.Clone();
                next.Apply(move);
                if (MoveGenerator.IsCheckmated(next, side.Opponent())) return move;
            }

            if (rng.NextDouble() < BlunderChance) return moves[rng.Next(moves.Count)];

            Move best = moves[0];
            double bestScore = double.NegativeInfinity;
            foreach (var move in moves)
            {
                var next = board.Clone();
                next.Apply(move);
                double score = ScoreAfterReply(next, side) + Positional(board, move) + (rng.NextDouble() * 2 - 1) * Noise;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        /// <summary>
        /// 第三阶段：双方都按子力看两步。攻城方能走进城堡就进；城门开了以后离城堡越近越好。
        /// 守城方（朋友）的守卫被吃光前不会主动送子，剩得少时更敢往前冲（离玩家的棋子近一点加分）。
        /// </summary>
        Move? ChooseStage3Move(Board board, Side side, List<Move> moves)
        {
            bool rescuer = side == board.Rescuer;
            if (rescuer)
            {
                foreach (var move in moves)
                    if (board.IsCastle(move.To)) return move;
            }

            if (rng.NextDouble() < BlunderChance) return moves[rng.Next(moves.Count)];

            Move best = moves[0];
            double bestScore = double.NegativeInfinity;
            foreach (var move in moves)
            {
                var next = board.Clone();
                next.Apply(move);
                double score = ScoreAfterReply(next, side);
                var piece = board.GetPiece(move.PieceId);
                if (rescuer && board.GateOpen) score -= 0.5 * board.DistanceToCastle(move.To);
                if (!rescuer && board.GuardCount <= 3) score -= 0.1 * NearestEnemy(board, move.To, side);
                if (piece.Type == PieceType.King) score -= 0.3;
                score += (rng.NextDouble() * 2 - 1) * Noise;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        static int NearestEnemy(Board board, Coord c, Side me)
        {
            int best = 8;
            foreach (var p in board.PiecesOf(me.Opponent())) best = Math.Min(best, p.Pos.DistanceTo(c));
            return best;
        }

        /// <summary>对方回应时会吃掉我们最值钱的子。</summary>
        static double ScoreAfterReply(Board board, Side me)
        {
            double material = Material(board, me);
            int worstLoss = 0;
            foreach (var reply in MoveGenerator.Generate(board, me.Opponent()))
            {
                var target = board.PieceAt(reply.To);
                if (target != null) worstLoss = Math.Max(worstLoss, Value(board, target));
            }
            return material - worstLoss;
        }

        /// <summary>一点点位置分：鼓励兵往前走、马和象离开底线，不鼓励王到处乱走。</summary>
        static double Positional(Board board, Move move)
        {
            var piece = board.GetPiece(move.PieceId);
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    return 0.05 * Math.Abs(move.To.Y - move.From.Y);
                case PieceType.Knight:
                case PieceType.Bishop:
                    return move.From.Y == piece.Side.HomeRank() ? 0.15 : 0;
                case PieceType.King:
                    return -0.3;
                default:
                    return 0;
            }
        }

        static double Material(Board board, Side me)
        {
            double score = 0;
            foreach (var p in board.AllPieces)
            {
                if (!p.OnBoard) continue;
                int value = Value(board, p);
                score += p.Side == me ? value : -value;
            }
            return score;
        }

        /// <summary>子力。第三阶段王是普通棋子，算 3；城堡里的国王、王后不算（谁也碰不到，城门开了也只是夺权的门票）。</summary>
        static int Value(Board board, Piece p)
        {
            if (board.InCastle(p)) return 0;
            if (board.Stage3 && p.Type == PieceType.King) return 3;
            return PieceValues.Of(p.Type);
        }
    }
}
