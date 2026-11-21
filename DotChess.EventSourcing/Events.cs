using System;

namespace DotChess.EventSourcing
{
    public abstract class DomainEvent
    {
        public string MatchId { get; set; }
        public int EventVersion { get; set; }
        public DateTime OccurredAt { get; set; }
        public string EventType => GetType().Name;

        protected DomainEvent(string matchId, int eventVersion)
        {
            MatchId = matchId;
            EventVersion = eventVersion;
            OccurredAt = DateTime.UtcNow;
        }
    }

    public class MatchCreatedEvent : DomainEvent
    {
        public string WhitePlayer { get; set; }
        public string BlackPlayer { get; set; }

        public MatchCreatedEvent(string matchId, string whitePlayer, string blackPlayer)
            : base(matchId, 1)
        {
            WhitePlayer = whitePlayer;
            BlackPlayer = blackPlayer;
        }
    }

    public class MovePlayedEvent : DomainEvent
    {
        public ChessPieceId PieceId { get; set; }
        public ChessPosition TargetPos { get; set; }
        public ChessResultF MoveResult { get; set; }
        public ChessColor PlayerColor { get; set; }
        public string MoveNotation { get; set; }

        public MovePlayedEvent(string matchId, int eventVersion, ChessPieceId pieceId,
            ChessPosition targetPos, ChessResultF moveResult, ChessColor color, string notation)
            : base(matchId, eventVersion)
        {
            PieceId = pieceId;
            TargetPos = targetPos;
            MoveResult = moveResult;
            PlayerColor = color;
            MoveNotation = notation;
        }
    }

    public class MoveRejectedEvent : DomainEvent
    {
        public ChessPieceId PieceId { get; set; }
        public ChessPosition TargetPos { get; set; }
        public ChessResultF RejectReason { get; set; }
        public string ErrorMessage { get; set; }

        public MoveRejectedEvent(string matchId, int eventVersion, ChessPieceId pieceId,
            ChessPosition targetPos, ChessResultF reason, string errorMsg)
            : base(matchId, eventVersion)
        {
            PieceId = pieceId;
            TargetPos = targetPos;
            RejectReason = reason;
            ErrorMessage = errorMsg;
        }
    }

    public class MatchEndedEvent : DomainEvent
    {
        public ChessColorId Winner { get; set; }
        public string Reason { get; set; }

        public MatchEndedEvent(string matchId, int eventVersion, ChessColorId winner, string reason)
            : base(matchId, eventVersion)
        {
            Winner = winner;
            Reason = reason;
        }
    }
}
