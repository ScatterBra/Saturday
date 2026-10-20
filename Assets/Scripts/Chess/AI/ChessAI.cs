using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 弱 AI：能将死就将死；偶尔随手乱走（犯错）；其余时候看两步——自己这一步之后，
    /// 对方最贪心的回应会吃掉什么，选子力最划算的走法，再加一点随机噪声。
    /// 走法都是走法生成器给的合法走法，所以它不会送将，也不会吃王。
    /// AI 不知道有哪些规则，它只调用走法生成器，所以自然会遵守冻结、城堡、岩浆等状态。
    /// 不过第三阶段的打分知道“马要进城堡、要先献祭”这个目标。
    /// </summary>
    public sealed class ChessAI
    {
        /// <summary>每一步有这么大的概率随手乱走。</summary>
        public double BlunderChance = 0.2;
        /// <summary>评分噪声幅度（单位：兵的价值）。</summary>
        public double Noise = 0.6;

        const double CastleLost = -1000;

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
        /// 第三阶段：朋友（守城方）只守城堡——尽量让玩家的马离城堡远、把它打进岩浆，绝不放玩家下一步进城堡。
        /// 玩家一方（测试里也由 AI 代下）：先凑够献祭，再让马往城堡靠近，能进城堡就进。
        /// </summary>
        Move? ChooseStage3Move(Board board, Side side, List<Move> moves)
        {
            bool rescuer = side == board.Rescuer;
            if (rescuer)
            {
                foreach (var move in moves)
                    if (board.IsCastle(move.To) && board.GetPiece(move.PieceId).Type == PieceType.Knight) return move;
            }

            if (rng.NextDouble() < BlunderChance) return moves[rng.Next(moves.Count)];

            Move best = moves[0];
            double bestScore = double.NegativeInfinity;
            foreach (var move in moves)
            {
                var next = board.Clone();
                var result = next.Apply(move);
                double score = rescuer ? RescuerScore(next, side, result) : GuardScore(next, side);
                score += (rng.NextDouble() * 2 - 1) * Noise;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        static double GuardScore(Board board, Side me)
        {
            foreach (var reply in MoveGenerator.Generate(board, me.Opponent()))
                if (board.IsCastle(reply.To)) return CastleLost;
            return 1.5 * KnightDistance(board) + Material(board, me);
        }

        static double RescuerScore(Board board, Side me, MoveResult result)
        {
            // 分数要连续：完成献祭（+20）永远比停在差一个的时候好，之后再看马离城堡多近
            double score = 0.3 * Material(board, me) + 6 * Math.Min(board.Sacrifices, board.SacrificesNeeded);
            if (result.Sacrificed && result.Mover.Type == PieceType.Knight) score -= 20;
            if (board.TaskComplete) score += 20 - 1.5 * KnightDistance(board);
            return score;
        }

        static readonly Coord[] KnightJumps =
        {
            new Coord(1, 2), new Coord(2, 1), new Coord(2, -1), new Coord(1, -2),
            new Coord(-1, -2), new Coord(-2, -1), new Coord(-2, 1), new Coord(-1, 2),
        };

        /// <summary>
        /// 玩家最靠近城堡的那匹马还要跳几步才能进城堡（马无视高度，只按 L 形跳数算，不管路上有没有子）；
        /// 没有马时给一个大数。
        /// </summary>
        static double KnightDistance(Board board)
        {
            var steps = new int[64];
            for (int i = 0; i < 64; i++) steps[i] = -1;
            var queue = new Queue<Coord>();
            for (int i = 0; i < 64; i++)
            {
                if (!board.IsCastle(Coord.FromIndex(i))) continue;
                steps[i] = 0;
                queue.Enqueue(Coord.FromIndex(i));
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var jump in KnightJumps)
                {
                    var next = c + jump;
                    if (!next.IsValid || steps[next.Index] >= 0) continue;
                    steps[next.Index] = steps[c.Index] + 1;
                    queue.Enqueue(next);
                }
            }

            double best = 12;
            foreach (var p in board.PiecesOf(board.Rescuer))
                if (p.Type == PieceType.Knight && steps[p.Pos.Index] >= 0) best = Math.Min(best, 2 * steps[p.Pos.Index]);
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
                if (target != null) worstLoss = Math.Max(worstLoss, PieceValues.Of(target.Type));
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
