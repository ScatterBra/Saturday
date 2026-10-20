using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 走法生成。第一、二阶段按正式的将军规则：不能走让自己的王被攻击的棋（被将军必须应将），王永远不会被吃；
    /// 去掉了王车易位和吃过路兵，兵升变自动变后。
    /// 规则和地形带来的状态都在这里统一处理，AI 和玩家用的是同一套：
    /// - 冻结的子不能动。
    /// - 第三阶段：
    ///   * 马照常跳 L 形，无视高度（可以直接跳上高原、跳进城堡）。
    ///   * 其他棋子：高原（高度 &gt; 0 的格子）和城堡挡住滑行；站在高原上只能前后左右走一格，
    ///     一次最多爬升或下降一级；平地上的棋子也能前后左右一格爬上高度 1。
    ///   * 只有我方的马能进城堡，而且要先完成献祭。
    ///   * 献祭完成前，我方棋子只能落在岩浆这一侧，或者走进岩浆献祭（王除外）；
    ///     完成后我方棋子可以站在岩浆上、滑过岩浆。对手的棋子不上岩浆。
    ///   * 对手不攻击我方的王；对手打我方棋子时要能把它击飞，否则这步不能走。
    /// </summary>
    public static class MoveGenerator
    {
        static readonly Coord[] KnightJumps =
        {
            new Coord(1, 2), new Coord(2, 1), new Coord(2, -1), new Coord(1, -2),
            new Coord(-1, -2), new Coord(-2, -1), new Coord(-2, 1), new Coord(-1, 2),
        };
        static readonly Coord[] Straight = { new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1) };
        static readonly Coord[] Diagonal = { new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1) };
        static readonly Coord[] AllDirections =
        {
            new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1),
            new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1),
        };

        public static List<Move> Generate(Board board, Side side)
        {
            var moves = new List<Move>();
            foreach (var piece in board.PiecesOf(side)) AddMoves(board, piece, moves);
            RemoveIllegal(board, side, moves);
            return moves;
        }

        public static List<Move> MovesFor(Board board, Piece piece)
        {
            var moves = new List<Move>();
            if (piece.OnBoard) AddMoves(board, piece, moves);
            RemoveIllegal(board, piece.Side, moves);
            return moves;
        }

        public static bool HasAnyMove(Board board, Side side) => Generate(board, side).Count > 0;

        /// <summary>这一方的王正被攻击（被将军）。冻结的子不算在攻击。</summary>
        public static bool InCheck(Board board, Side side)
        {
            var king = board.King(side);
            if (king == null) return false;
            var attacks = new List<Move>();
            foreach (var piece in board.PiecesOf(side.Opponent())) AddMoves(board, piece, attacks);
            foreach (var move in attacks)
                if (move.To == king.Pos) return true;
            return false;
        }

        /// <summary>被将死：正被将军，而且没有任何合法走法。</summary>
        public static bool IsCheckmated(Board board, Side side) => InCheck(board, side) && !HasAnyMove(board, side);

        /// <summary>
        /// 第一、二阶段的将军规则：去掉吃王的走法，以及走完之后自己的王会被攻击的走法。
        /// 第三阶段不用：朋友不攻击王，玩家的王本来就不会被将军。
        /// </summary>
        static void RemoveIllegal(Board board, Side side, List<Move> moves)
        {
            if (board.Stage3 || moves.Count == 0) return;
            moves.RemoveAll(move =>
            {
                var target = board.PieceAt(move.To);
                if (target != null && target.Type == PieceType.King) return true;
                var next = board.Clone();
                next.Apply(move);
                return InCheck(next, side);
            });
        }

        static void AddMoves(Board board, Piece piece, List<Move> moves)
        {
            if (piece.IsFrozen) return;
            var from = piece.Pos;
            if (board.Stage3 && piece.Type != PieceType.Knight && board.Height(from) > 0)
            {
                AddPlateauSteps(board, piece, from, moves);
                return;
            }
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(board, piece, from, moves);
                    break;
                case PieceType.Knight:
                    AddJumps(board, piece, from, moves);
                    break;
                case PieceType.King:
                    AddSteps(board, piece, from, AllDirections, moves);
                    break;
                case PieceType.Bishop:
                    AddSlides(board, piece, from, Diagonal, moves);
                    break;
                case PieceType.Rook:
                    AddSlides(board, piece, from, Straight, moves);
                    break;
                case PieceType.Queen:
                    AddSlides(board, piece, from, AllDirections, moves);
                    break;
            }
            if (board.Stage3 && piece.Type != PieceType.Knight) AddClimbSteps(board, piece, from, moves);
        }

        /// <summary>落点检查结果：能不能落，以及（滑行时）要不要在这里停下。</summary>
        enum Landing { Free, Capture, Sacrifice, Blocked }

        /// <summary>平地上的普通走法能不能落在 to。高原、城堡只能靠一格一格爬上去。</summary>
        static Landing Check(Board board, Piece piece, Coord from, Coord to)
        {
            if (!to.IsValid) return Landing.Blocked;
            if (board.Stage3 && board.Height(to) > 0) return Landing.Blocked;
            return CheckCommon(board, piece, from, to);
        }

        /// <summary>高度以外的通用检查：岩浆、我方活动范围、占位。</summary>
        static Landing CheckCommon(Board board, Piece piece, Coord from, Coord to)
        {
            if (board.IsCastle(to))
            {
                bool mayEnter = piece.Side == board.Rescuer && piece.Type == PieceType.Knight && board.TaskComplete;
                return mayEnter && board.IsEmpty(to) ? Landing.Free : Landing.Blocked;
            }
            if (board.IsLava(to))
            {
                // 献祭完成后我方可以站在岩浆上；对手的棋子不上岩浆
                if (board.Stage3 && piece.Side == board.Rescuer && board.TaskComplete) return OccupantCheck(board, piece, from, to);
                return CanSacrifice(board, piece) ? Landing.Sacrifice : Landing.Blocked;
            }
            if (board.Stage3 && piece.Side == board.Rescuer && !board.TaskComplete && !board.InPlayerZone(to))
                return Landing.Blocked;
            return OccupantCheck(board, piece, from, to);
        }

        static Landing OccupantCheck(Board board, Piece piece, Coord from, Coord to)
        {
            var target = board.PieceAt(to);
            if (target == null) return Landing.Free;
            return CanAttack(board, piece, target, from) ? Landing.Capture : Landing.Blocked;
        }

        static bool CanSacrifice(Board board, Piece piece) =>
            board.Stage3 && piece.Side == board.Rescuer && !board.TaskComplete && piece.Type != PieceType.King;

        static bool CanAttack(Board board, Piece attacker, Piece target, Coord from)
        {
            if (target.Side == attacker.Side) return false;
            if (board.Stage3 && attacker.Side != board.Rescuer && target.Side == board.Rescuer)
            {
                // 第三阶段对手只守城堡，不攻击我方的王；打我方棋子要能把它击飞
                if (target.Type == PieceType.King) return false;
                return Knockback.FindLanding(board, target, from) != null;
            }
            return true;
        }

        static void AddPawnMoves(Board board, Piece pawn, Coord from, List<Move> moves)
        {
            int dir = pawn.Side.Forward();
            var one = new Coord(from.X, from.Y + dir);
            var oneCheck = Check(board, pawn, from, one);
            if (oneCheck == Landing.Free || oneCheck == Landing.Sacrifice)
            {
                moves.Add(new Move(pawn.Id, from, one));
                var two = new Coord(from.X, from.Y + 2 * dir);
                if (oneCheck == Landing.Free && from.Y == pawn.Side.PawnRank() &&
                    Check(board, pawn, from, two) == Landing.Free)
                    moves.Add(new Move(pawn.Id, from, two));
            }
            for (int dx = -1; dx <= 1; dx += 2)
            {
                var diag = new Coord(from.X + dx, from.Y + dir);
                if (Check(board, pawn, from, diag) == Landing.Capture) moves.Add(new Move(pawn.Id, from, diag));
            }
        }

        /// <summary>马跳 L 形。第三阶段无视高度，所以只做高度以外的检查。</summary>
        static void AddJumps(Board board, Piece piece, Coord from, List<Move> moves)
        {
            foreach (var offset in KnightJumps)
            {
                var to = from + offset;
                if (!to.IsValid) continue;
                if (CheckCommon(board, piece, from, to) != Landing.Blocked) moves.Add(new Move(piece.Id, from, to));
            }
        }

        static void AddSteps(Board board, Piece piece, Coord from, Coord[] offsets, List<Move> moves)
        {
            foreach (var offset in offsets)
            {
                var to = from + offset;
                if (Check(board, piece, from, to) != Landing.Blocked) moves.Add(new Move(piece.Id, from, to));
            }
        }

        static void AddSlides(Board board, Piece piece, Coord from, Coord[] directions, List<Move> moves)
        {
            foreach (var dir in directions)
            {
                var to = from + dir;
                while (true)
                {
                    var landing = Check(board, piece, from, to);
                    if (landing == Landing.Blocked) break;
                    moves.Add(new Move(piece.Id, from, to));
                    if (landing != Landing.Free) break; // 吃子、献祭后停下
                    to = to + dir;
                }
            }
        }

        /// <summary>站在高原上（马除外）：只能前后左右走一格，高度差最多 1。</summary>
        static void AddPlateauSteps(Board board, Piece piece, Coord from, List<Move> moves)
        {
            foreach (var dir in Straight)
            {
                var to = from + dir;
                if (!to.IsValid || System.Math.Abs(board.Height(to) - board.Height(from)) > 1) continue;
                if (CheckCommon(board, piece, from, to) != Landing.Blocked) moves.Add(new Move(piece.Id, from, to));
            }
        }

        /// <summary>平地上的棋子可以前后左右一格爬上高度 1 的高原（普通走法到不了高原）。</summary>
        static void AddClimbSteps(Board board, Piece piece, Coord from, List<Move> moves)
        {
            foreach (var dir in Straight)
            {
                var to = from + dir;
                if (!to.IsValid || board.Height(to) != board.Height(from) + 1) continue;
                if (CheckCommon(board, piece, from, to) != Landing.Blocked) moves.Add(new Move(piece.Id, from, to));
            }
        }
    }
}
