using System;
using System.Collections.Generic;
using System.Linq;

namespace DotChess.EventSourcing
{
    /// <summary>
    /// Agregado que representa uma partida de xadrez.
    /// Pode ser reconstruído a partir de eventos usando ApplyEvent().
    /// </summary>
    public class ChessMatch
    {
        public string MatchId { get; }
        public string WhitePlayer { get; private set; }
        public string BlackPlayer { get; private set; }
        public ChessGame Game { get; private set; }
        public ChessGameInfo GameInfo { get; private set; }
        public int EventVersion { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public bool IsEnded { get; private set; }
        public ChessColorId Winner { get; private set; }

        private List<DomainEvent> _uncommittedEvents = new List<DomainEvent>();

        public ChessMatch(string matchId, string whitePlayer, string blackPlayer)
        {
            MatchId = matchId;
            WhitePlayer = whitePlayer;
            BlackPlayer = blackPlayer;
            Game = new ChessGame();
            GameInfo = Game.Info;
            EventVersion = 0;
            CreatedAt = DateTime.UtcNow;
            IsEnded = false;
            Winner = ChessColorId.Undefined;

            // Raise event
            var evt = new MatchCreatedEvent(matchId, whitePlayer, blackPlayer);
            _uncommittedEvents.Add(evt);
            ApplyEvent(evt);
        }

        /// <summary>
        /// Tenta fazer uma jogada. Se válida, adiciona MovePlayedEvent aos uncommitted.
        /// Se inválida, adiciona MoveRejectedEvent.
        /// </summary>
        public ChessResultF PlayMove(ChessPieceId pieceId, ChessPosition targetPos, bool promoteToKnight = false)
        {
            if (IsEnded)
            {
                var rejected = new MoveRejectedEvent(MatchId, EventVersion + 1, pieceId, targetPos,
                    ChessResultF.Invalid, "Match has ended");
                _uncommittedEvents.Add(rejected);
                return ChessResultF.Invalid;
            }

            var piece = Game.GetPiece(pieceId);
            if (piece == ChessPiece.kNull || !piece.IsOnBoard)
            {
                var rejected = new MoveRejectedEvent(MatchId, EventVersion + 1, pieceId, targetPos,
                    ChessResultF.Invalid, "Piece not found or captured");
                _uncommittedEvents.Add(rejected);
                return ChessResultF.Invalid;
            }

            ChessResultF result = Game.Move(pieceId, targetPos, promoteToKnight);

            if (!result.IsAllowedMove())
            {
                var rejected = new MoveRejectedEvent(MatchId, EventVersion + 1, pieceId, targetPos,
                    result, $"Move rejected: {result}");
                _uncommittedEvents.Add(rejected);
                return result;
            }

            // Move was successful
            string notation = Game.Moves.Count > 0 ? Game.Moves[Game.Moves.Count - 1].ToString() : "";
            var evt = new MovePlayedEvent(MatchId, EventVersion + 1, pieceId, targetPos, result,
                piece.Color, notation);
            _uncommittedEvents.Add(evt);
            ApplyEvent(evt);

            // Check if game ended
            if (result.IsComplete())
            {
                ChessColorId winner = ChessColorId.Undefined;
                string reason = "";

                if (result.IsAny(ChessResultF.Checkmate))
                {
                    winner = result.IsAny(ChessResultF.ColorW) ? ChessColorId.Black : ChessColorId.White;
                    reason = "Checkmate";
                }
                else if (result.IsAny(ChessResultF.Resigned))
                {
                    winner = result.IsAny(ChessResultF.ColorW) ? ChessColorId.Black : ChessColorId.White;
                    reason = "Resignation";
                }
                else if (result.IsAny(ChessResultF.Stalemate))
                {
                    winner = ChessColorId.Stalemate;
                    reason = "Stalemate";
                }

                var endEvt = new MatchEndedEvent(MatchId, EventVersion + 2, winner, reason);
                _uncommittedEvents.Add(endEvt);
                ApplyEvent(endEvt);
            }

            return result;
        }

        /// <summary>
        /// Aplica um evento ao estado do agregado.
        /// Usado durante replay e após comandos bem-sucedidos.
        /// </summary>
        public void ApplyEvent(DomainEvent evt)
        {
            if (evt is MatchCreatedEvent ce)
            {
                WhitePlayer = ce.WhitePlayer;
                BlackPlayer = ce.BlackPlayer;
                EventVersion = ce.EventVersion;
                CreatedAt = ce.OccurredAt;
            }
            else if (evt is MovePlayedEvent me)
            {
                EventVersion = me.EventVersion;
            }
            else if (evt is MatchEndedEvent ee)
            {
                IsEnded = true;
                Winner = ee.Winner;
                EventVersion = ee.EventVersion;
            }
            else if (evt is MoveRejectedEvent)
            {
                // Rejected moves don't change state
            }
        }

        /// <summary>
        /// Retorna e limpa eventos não persistidos.
        /// </summary>
        public IEnumerable<DomainEvent> GetUncommittedEvents()
        {
            return _uncommittedEvents.ToList();
        }

        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        /// <summary>
        /// Reconstrói o agregado a partir de um histórico de eventos.
        /// </summary>
        public static ChessMatch LoadFromHistory(IEnumerable<DomainEvent> events)
        {
            if (!events.Any())
                throw new InvalidOperationException("No events to load");

            var firstEvent = events.First();
            if (!(firstEvent is MatchCreatedEvent ce))
                throw new InvalidOperationException("First event must be MatchCreatedEvent");

            var match = new ChessMatch(ce.MatchId, ce.WhitePlayer, ce.BlackPlayer);
            match._uncommittedEvents.Clear(); // Clear creation event
            match.EventVersion = 0;

            foreach (var evt in events)
            {
                match.ApplyEvent(evt);
                match._uncommittedEvents.Clear();
            }

            return match;
        }
    }
}
