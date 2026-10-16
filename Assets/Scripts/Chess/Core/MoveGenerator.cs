using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 走法生成。简化规则：胜利条件是吃掉国王，没有“将军”的概念，所以不用检查走完是否被将军；
    /// 去掉了王车易位和吃过路兵；兵升变自动变后。
    /// 规则带来的状态在这里统一处理：冻结的子不能动，城堡不能被吃，岩浆格不能进入也不能滑过。
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

        /// <summary>这一方所有合法走法（包括城堡里的棋子走出来）。</summary>
        public static List<Move> Generate(Board board, Side side)
        {
            var moves = new List<Move>();
            foreach (var piece in board.PiecesOf(side))
            {
                if (piece.IsHidden) continue;
                AddMoves(board, piece, piece.Pos, false, moves);
                if (piece.HasGuest) AddMoves(board, board.GetPiece(piece.GuestId), piece.Pos, true, moves);
            }
            return moves;
        }

        /// <summary>某一枚棋子的走法。藏在城堡里的棋子会从城堡所在格走出来。</summary>
        public static List<Move> MovesFor(Board board, Piece piece)
        {
            var moves = new List<Move>();
            if (piece.OnBoard) AddMoves(board, piece, piece.Pos, piece.IsHidden, moves);
            return moves;
        }

        public static bool HasAnyMove(Board board, Side side) => Generate(board, side).Count > 0;

        static void AddMoves(Board board, Piece piece, Coord from, bool guestExit, List<Move> moves)
        {
            if (piece.IsFrozen) return;
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(board, piece, from, guestExit, moves);
                    break;
                case PieceType.Knight:
                    AddSteps(board, piece, from, KnightJumps, guestExit, moves);
                    break;
                case PieceType.King:
                    AddSteps(board, piece, from, AllDirections, guestExit, moves);
                    break;
                case PieceType.Bishop:
                    AddSlides(board, piece, from, Diagonal, guestExit, moves);
                    break;
                case PieceType.Rook:
                    AddSlides(board, piece, from, Straight, guestExit, moves);
                    break;
                case PieceType.Queen:
                    AddSlides(board, piece, from, AllDirections, guestExit, moves);
                    break;
            }
        }

        static void AddPawnMoves(Board board, Piece pawn, Coord from, bool guestExit, List<Move> moves)
        {
            int dir = pawn.Side.Forward();
            var one = new Coord(from.X, from.Y + dir);
            if (one.IsValid && !board.IsLava(one) && board.IsEmpty(one))
            {
                moves.Add(new Move(pawn.Id, from, one, guestExit));
                var two = new Coord(from.X, from.Y + 2 * dir);
                if (from.Y == pawn.Side.PawnRank() && two.IsValid && !board.IsLava(two) && board.IsEmpty(two))
                    moves.Add(new Move(pawn.Id, from, two, guestExit));
            }
            for (int dx = -1; dx <= 1; dx += 2)
            {
                var diag = new Coord(from.X + dx, from.Y + dir);
                if (!diag.IsValid) continue;
                var target = board.PieceAt(diag);
                if (target != null && CanCapture(pawn, target)) moves.Add(new Move(pawn.Id, from, diag, guestExit));
            }
        }

        static void AddSteps(Board board, Piece piece, Coord from, Coord[] offsets, bool guestExit, List<Move> moves)
        {
            foreach (var offset in offsets)
            {
                var to = from + offset;
                if (!to.IsValid || board.IsLava(to)) continue;
                var target = board.PieceAt(to);
                if (target == null || CanCapture(piece, target)) moves.Add(new Move(piece.Id, from, to, guestExit));
            }
        }

        static void AddSlides(Board board, Piece piece, Coord from, Coord[] directions, bool guestExit, List<Move> moves)
        {
            foreach (var dir in directions)
            {
                var to = from + dir;
                while (to.IsValid && !board.IsLava(to))
                {
                    var target = board.PieceAt(to);
                    if (target == null)
                    {
                        moves.Add(new Move(piece.Id, from, to, guestExit));
                    }
                    else
                    {
                        if (CanCapture(piece, target)) moves.Add(new Move(piece.Id, from, to, guestExit));
                        break;
                    }
                    to = to + dir;
                }
            }
        }

        static bool CanCapture(Piece attacker, Piece target) => target.Side != attacker.Side && !target.IsCastle;
    }
}
