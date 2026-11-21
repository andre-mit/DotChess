using DotChess.EventSourcing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotChess.Api;

public class ChessMatchService
{
    private readonly InMemoryEventStore _eventStore;
    private readonly Dictionary<string, ChessMatch> _matchCache = new();
    private readonly object _cacheLock = new();

    public ChessMatchService()
    {
        _eventStore = new InMemoryEventStore();
    }

    public string CreateMatch(string whitePlayer, string blackPlayer)
    {
        var matchId = Guid.NewGuid().ToString("N").Substring(0, 12);
        var match = new ChessMatch(matchId, whitePlayer, blackPlayer);

        var events = match.GetUncommittedEvents().ToList();
        _eventStore.AppendEvents(matchId, events);
        match.ClearUncommittedEvents();

        lock (_cacheLock)
        {
            _matchCache[matchId] = match;
        }

        return matchId;
    }

    public MoveResponse PlayMove(string matchId, PlayMoveRequest request)
    {
        var match = GetOrReplayMatch(matchId);
        if (match == null)
            return new MoveResponse
            {
                Success = false,
                ErrorMessage = "Match not found"
            };

        var pieceId = (ChessPieceId)request.FromPieceId;
        var targetPos = new ChessPosition((byte)request.TargetX, (byte)request.TargetY);

        var result = match.PlayMove(pieceId, targetPos, request.PromoteToKnight);
        var events = match.GetUncommittedEvents().ToList();

        if (!events.Any())
            return new MoveResponse
            {
                Success = false,
                ErrorMessage = $"Move rejected: {result}",
                CurrentVersion = _eventStore.GetCurrentVersion(matchId)
            };

        try
        {
            _eventStore.AppendEvents(matchId, events);
        }
        catch (ConcurrencyException ex)
        {
            return new MoveResponse
            {
                Success = false,
                ErrorMessage = $"Concurrency conflict: {ex.Message}",
                CurrentVersion = _eventStore.GetCurrentVersion(matchId)
            };
        }

        lock (_cacheLock)
        {
            _matchCache[matchId] = match;
        }

        match.ClearUncommittedEvents();

        return new MoveResponse
        {
            Success = true,
            MoveNotation = events.OfType<MovePlayedEvent>().FirstOrDefault()?.MoveNotation ?? "",
            Result = result.ToString(),
            CurrentVersion = _eventStore.GetCurrentVersion(matchId)
        };
    }

    public MatchStatusResponse GetMatchStatus(string matchId)
    {
        var match = GetOrReplayMatch(matchId);
        if (match == null)
            return null;

        var game = match.Game;
        var validMoves = new List<string>();

        // Get valid moves for all pieces of current player
        for (int i = 0; i < 32; i++)
        {
            var piece = game.GetPiece((ChessPieceId)i);
            if (!piece.IsOnBoard || piece.Color != game.TurnColor)
                continue;

            var moves = game.GetValidMovesFor((ChessPieceId)i);
            foreach (var move in moves)
            {
                validMoves.Add($"{piece.Pos.Notation}-{move.ToPos.Notation}");
            }
        }

        return new MatchStatusResponse
        {
            MatchId = matchId,
            WhitePlayer = match.WhitePlayer,
            BlackPlayer = match.BlackPlayer,
            MoveCount = game.MoveCount,
            CurrentTurn = game.TurnColor == ChessColor.kWhite ? "White" : "Black",
            FEN = game.Board.GetFEN(false),
            IsEnded = match.IsEnded,
            Winner = match.Winner.ToString(),
            ValidMoves = validMoves.ToArray()
        };
    }

    public MatchHistoryResponse GetMatchHistory(string matchId)
    {
        var events = _eventStore.GetEvents(matchId).ToList();
        if (!events.Any())
            return null;

        var match = GetOrReplayMatch(matchId);
        var moveEvents = events.OfType<MovePlayedEvent>().ToList();

        var moves = new List<MoveHistoryItem>();
        var tempMatch = new ChessMatch(matchId, match.WhitePlayer, match.BlackPlayer);

        int moveNumber = 0;
        string whiteMoveNotation = "";

        foreach (var moveEvent in moveEvents)
        {
            if (moveEvent.PlayerColor == ChessColor.kWhite)
            {
                whiteMoveNotation = moveEvent.MoveNotation;
            }
            else
            {
                moveNumber++;
                moves.Add(new MoveHistoryItem
                {
                    MoveNumber = moveNumber,
                    WhiteMove = whiteMoveNotation,
                    BlackMove = moveEvent.MoveNotation,
                    FENAfterMove = tempMatch.Game.Board.GetFEN(false)
                });
            }

            // Replay move on temp match
            tempMatch.PlayMove(moveEvent.PieceId, moveEvent.TargetPos,
                moveEvent.MoveResult.IsAny(ChessResultF.PromoteN));
        }

        return new MatchHistoryResponse
        {
            MatchId = matchId,
            TotalMoves = moveNumber,
            Moves = moves.ToArray(),
            CreatedAt = match.CreatedAt
        };
    }

    private ChessMatch GetOrReplayMatch(string matchId)
    {
        lock (_cacheLock)
        {
            if (_matchCache.TryGetValue(matchId, out var cached))
                return cached;
        }

        try
        {
            var match = _eventStore.Replay(matchId);
            lock (_cacheLock)
            {
                _matchCache[matchId] = match;
            }
            return match;
        }
        catch
        {
            return null;
        }
    }
}
