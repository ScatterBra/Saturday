using System;
using System.Collections.Generic;

namespace SaturdayGame.Chess
{
    /// <summary>
    /// 棋盘状态：棋子、岩浆格、轮到谁、第几回合。纯数据，可以 Clone 给 AI 推演。
    /// 一个“回合”是双方各走一步（full move），白方先走。
    /// </summary>
    public sealed class Board
    {
        readonly Piece[] squares = new Piece[64];
        readonly bool[] lava = new bool[64];
        readonly List<Piece> pieces = new List<Piece>();

        public Side SideToMove { get; private set; } = Side.White;
        public int FullMove { get; private set; } = 1;

        /// <summary>整局出现过的所有棋子（包括被吃掉的），下标就是 Piece.Id。</summary>
        public IReadOnlyList<Piece> AllPieces => pieces;

        public static Board CreateStandard()
        {
            var board = new Board();
            PieceType[] backRank =
            {
                PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
                PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook,
            };
            foreach (var side in new[] { Side.White, Side.Black })
            {
                for (int x = 0; x < 8; x++)
                {
                    board.AddPiece(backRank[x], side, new Coord(x, side.HomeRank()));
                    board.AddPiece(PieceType.Pawn, side, new Coord(x, side.PawnRank()));
                }
            }
            return board;
        }

        public Piece AddPiece(PieceType type, Side side, Coord at)
        {
            if (squares[at.Index] != null) throw new InvalidOperationException($"{at} is occupied");
            var piece = new Piece { Id = pieces.Count, Type = type, Side = side, Pos = at, StartPos = at };
            pieces.Add(piece);
            squares[at.Index] = piece;
            return piece;
        }

        public Piece GetPiece(int id) => pieces[id];

        /// <summary>这个格子上看得见的棋子（藏在城堡里的不算），没有则为 null。</summary>
        public Piece PieceAt(Coord c) => squares[c.Index];

        public bool IsEmpty(Coord c) => squares[c.Index] == null;

        public bool IsLava(Coord c) => lava[c.Index];

        public void SetLava(Coord c)
        {
            if (squares[c.Index] != null) throw new InvalidOperationException($"Lava on occupied square {c}");
            lava[c.Index] = true;
        }

        /// <summary>这一方还在场上的棋子（包括藏在城堡里的）。</summary>
        public IEnumerable<Piece> PiecesOf(Side side)
        {
            foreach (var p in pieces)
                if (p.OnBoard && p.Side == side) yield return p;
        }

        /// <summary>这一方被吃掉的棋子。</summary>
        public IEnumerable<Piece> CapturedOf(Side side)
        {
            foreach (var p in pieces)
                if (!p.OnBoard && p.Side == side) yield return p;
        }

        public Piece King(Side side)
        {
            foreach (var p in pieces)
                if (p.OnBoard && p.Side == side && p.Type == PieceType.King) return p;
            return null;
        }

        /// <summary>把棋子从棋盘上拿走（被吃）。城堡被拿走时里面藏的棋子一起拿走。</summary>
        public void RemovePiece(Piece piece)
        {
            if (!piece.OnBoard) return;
            if (piece.IsHidden)
            {
                var host = pieces[piece.HostId];
                host.GuestId = -1;
                piece.IsHidden = false;
                piece.HostId = -1;
            }
            else if (squares[piece.Pos.Index] == piece)
            {
                squares[piece.Pos.Index] = null;
            }

            if (piece.HasGuest)
            {
                var guest = pieces[piece.GuestId];
                piece.GuestId = -1;
                guest.IsHidden = false;
                guest.HostId = -1;
                guest.OnBoard = false;
            }
            piece.OnBoard = false;
            piece.FrozenTurns = 0;
            piece.FrozenSkin = null;
        }

        /// <summary>把被吃掉的棋子放回棋盘（复活）。</summary>
        public void RestorePiece(Piece piece, Coord at)
        {
            if (piece.OnBoard) throw new InvalidOperationException($"{piece} is already on the board");
            if (squares[at.Index] != null || lava[at.Index]) throw new InvalidOperationException($"Cannot restore onto {at}");
            piece.OnBoard = true;
            piece.Pos = at;
            piece.FrozenTurns = 0;
            piece.FrozenSkin = null;
            squares[at.Index] = piece;
        }

        /// <summary>让棋子藏进己方城堡。</summary>
        public void HideInCastle(Piece guest, Piece castle)
        {
            if (!castle.IsCastle || castle.HasGuest || castle.Side != guest.Side || !guest.IsVisible || guest.IsCastle)
                throw new InvalidOperationException($"{guest} cannot hide in {castle}");
            squares[guest.Pos.Index] = null;
            guest.IsHidden = true;
            guest.HostId = castle.Id;
            guest.Pos = castle.Pos;
            castle.GuestId = guest.Id;
        }

        /// <summary>
        /// 执行一步走法（不检查合法性，调用方负责只传入 MoveGenerator 生成的走法），然后交换走子方。
        /// </summary>
        public MoveResult Apply(Move move)
        {
            var mover = pieces[move.PieceId];
            var result = new MoveResult { Mover = mover, From = move.From, To = move.To, GuestExit = move.GuestExit };

            if (move.GuestExit)
            {
                var host = pieces[mover.HostId];
                host.GuestId = -1;
                mover.IsHidden = false;
                mover.HostId = -1;
            }
            else
            {
                squares[move.From.Index] = null;
            }

            var target = squares[move.To.Index];
            if (target != null)
            {
                RemovePiece(target);
                result.Captured = target;
            }

            squares[move.To.Index] = mover;
            mover.Pos = move.To;
            if (mover.HasGuest) pieces[mover.GuestId].Pos = move.To;

            // 兵升变：自动变后
            if (mover.Type == PieceType.Pawn && move.To.Y == mover.Side.PromotionRank())
            {
                mover.Type = PieceType.Queen;
                result.Promoted = true;
            }

            EndTurn();
            return result;
        }

        /// <summary>没有合法走法时跳过这一回合。</summary>
        public void Pass() => EndTurn();

        void EndTurn()
        {
            // 冻结在自己一方的回合结束时减一
            foreach (var p in pieces)
            {
                if (p.OnBoard && p.Side == SideToMove && p.FrozenTurns > 0)
                {
                    p.FrozenTurns--;
                    if (p.FrozenTurns == 0) p.FrozenSkin = null;
                }
            }
            if (SideToMove == Side.Black) FullMove++;
            SideToMove = SideToMove.Opponent();
        }

        public Board Clone()
        {
            var copy = new Board { SideToMove = SideToMove, FullMove = FullMove };
            Array.Copy(lava, copy.lava, lava.Length);
            foreach (var p in pieces)
            {
                var c = p.Clone();
                copy.pieces.Add(c);
                if (c.IsVisible) copy.squares[c.Pos.Index] = c;
            }
            return copy;
        }
    }
}
