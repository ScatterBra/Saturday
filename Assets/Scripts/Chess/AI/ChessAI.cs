using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 弱 AI：能吃王就吃王；偶尔随手乱走（犯错）；其余时候看两步——自己这一步之后，
    /// 对方最贪心的回应会吃掉什么，选子力最划算的走法，再加一点随机噪声。
    /// AI 不知道有哪些规则，它只调用走法生成器，所以自然会遵守冻结、城堡、岩浆等状态。
    /// </summary>
    public sealed class ChessAI
    {
        /// <summary>每一步有这么大的概率随手乱走。</summary>
        public double BlunderChance = 0.2;
        /// <summary>评分噪声幅度（单位：兵的价值）。</summary>
        public double Noise = 0.6;

        const double KingLost = -1000;

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

            foreach (var move in moves)
            {
                var target = board.PieceAt(move.To);
                if (target != null && target.Type == PieceType.King) return move;
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

        /// <summary>对方回应时会吃掉我们最值钱的子（如果能吃到王就直接判输）。</summary>
        static double ScoreAfterReply(Board board, Side me)
        {
            double material = Material(board, me);
            int worstLoss = 0;
            foreach (var reply in MoveGenerator.Generate(board, me.Opponent()))
            {
                var target = board.PieceAt(reply.To);
                if (target == null) continue;
                if (target.Type == PieceType.King) return KingLost;
                worstLoss = Math.Max(worstLoss, PieceValues.Of(target.Type));
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
                int value = PieceValues.Of(p.Type);
                score += p.Side == me ? value : -value;
            }
            return score;
        }
    }
}
