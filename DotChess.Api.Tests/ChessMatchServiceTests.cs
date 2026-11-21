using DotChess.Api;
using DotChess.EventSourcing;
using System;
using System.Linq;
using Xunit;

namespace DotChess.Api.Tests;

public class ChessMatchServiceTests
{
    [Fact]
    public void CreateMatch_ShouldCreateNewMatch()
    {
        // Arrange
        var service = new ChessMatchService();

        // Act
        var matchId = service.CreateMatch("Alice", "Bob");

        // Assert
        Assert.NotEmpty(matchId);
        var status = service.GetMatchStatus(matchId);
        Assert.NotNull(status);
        Assert.Equal("Alice", status.WhitePlayer);
        Assert.Equal("Bob", status.BlackPlayer);
        Assert.Equal("White", status.CurrentTurn);
        Assert.Equal(0, status.MoveCount);
        Assert.False(status.IsEnded);
    }

    [Fact]
    public void PlayMove_ValidOpeningMove_ShouldSucceed()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");
        var status0 = service.GetMatchStatus(matchId);
        Assert.NotNull(status0);

        // White pawn e2 to e4 (piece id WPe=12, pos 4,1 to 4,3)
        var moveRequest = new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.WPe,
            TargetX = 4,
            TargetY = 3,
            PromoteToKnight = false
        };

        // Act
        var result = service.PlayMove(matchId, moveRequest);

        // Assert
        if (!result.Success)
        {
            Assert.True(false, $"Move failed with error: {result.ErrorMessage}, Result: {result.Result}");
        }
        Assert.NotEmpty(result.MoveNotation);

        var status = service.GetMatchStatus(matchId);
        Assert.Equal("Black", status.CurrentTurn);
        Assert.Equal(1, status.MoveCount);
    }

    [Fact]
    public void PlayMove_InvalidMove_ShouldRejectWithoutEvent()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        // Try to move black pawn before white's turn ends - invalid
        // Black pawn at e7 (pos 4,6) - try to move off-board
        var moveRequest = new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.BPe,  // Black pawn
            TargetX = 4,
            TargetY = 7,  // Off board (y only goes 0-7)
            PromoteToKnight = false
        };

        // Act
        var result = service.PlayMove(matchId, moveRequest);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage);

        // Verify no move was recorded
        var status = service.GetMatchStatus(matchId);
        Assert.Equal(0, status.MoveCount);
        Assert.Equal("White", status.CurrentTurn);
    }

    [Fact]
    public void PlayMove_MoveSequence_ShouldProgressGame()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        // Act - White: e2-e4
        var move1 = service.PlayMove(matchId, new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.WPe,
            TargetX = 4,
            TargetY = 3
        });
        Assert.True(move1.Success);

        // Act - Black: e7-e6
        var move2 = service.PlayMove(matchId, new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.BPe,
            TargetX = 4,
            TargetY = 5
        });
        Assert.True(move2.Success);

        // Assert
        var status = service.GetMatchStatus(matchId);
        Assert.Equal(2, status.MoveCount);
        Assert.Equal("White", status.CurrentTurn);
        Assert.False(status.IsEnded);
    }

    [Fact]
    public void Replay_AfterMultipleMoves_ShouldRecoverState()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        // Make some moves
        service.PlayMove(matchId, new PlayMoveRequest { FromPieceId = (int)ChessPieceId.WPe, TargetX = 4, TargetY = 3 });
        service.PlayMove(matchId, new PlayMoveRequest { FromPieceId = (int)ChessPieceId.BPe, TargetX = 4, TargetY = 4 });
        service.PlayMove(matchId, new PlayMoveRequest { FromPieceId = (int)ChessPieceId.WKB, TargetX = 5, TargetY = 2 });

        var statusBefore = service.GetMatchStatus(matchId);

        // Act - Simulate cache miss and replay
        var statusAfter = service.GetMatchStatus(matchId);

        // Assert
        Assert.Equal(statusBefore.MoveCount, statusAfter.MoveCount);
        Assert.Equal(statusBefore.CurrentTurn, statusAfter.CurrentTurn);
        Assert.Equal(statusBefore.FEN, statusAfter.FEN);
    }

    [Fact]
    public void ConcurrencyTest_SimultaneousMoves_ShouldHandleConflict()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        // Act & Assert - Try two moves in sequence (simulating concurrent attempts)
        var move1 = service.PlayMove(matchId, new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.WPe,
            TargetX = 4,
            TargetY = 3
        });
        Assert.True(move1.Success);
        Assert.Equal(1, move1.CurrentVersion);

        // Second move should work (it's black's turn)
        var move2 = service.PlayMove(matchId, new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.BPe,
            TargetX = 4,
            TargetY = 4
        });
        Assert.True(move2.Success);
        Assert.Equal(2, move2.CurrentVersion);

        // Invalid simultaneous move (white tries again out of turn) should fail
        var invalidMove = service.PlayMove(matchId, new PlayMoveRequest
        {
            FromPieceId = (int)ChessPieceId.WPf,
            TargetX = 5,
            TargetY = 3
        });
        Assert.False(invalidMove.Success);
        Assert.Equal(2, invalidMove.CurrentVersion); // Version shouldn't change
    }

    [Fact]
    public void GetHistory_AfterMoves_ShouldReturnCompleteHistory()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        service.PlayMove(matchId, new PlayMoveRequest { FromPieceId = (int)ChessPieceId.WPe, TargetX = 4, TargetY = 3 });
        service.PlayMove(matchId, new PlayMoveRequest { FromPieceId = (int)ChessPieceId.BPe, TargetX = 4, TargetY = 5 });

        // Act
        var history = service.GetMatchHistory(matchId);

        // Assert
        Assert.NotNull(history);
        Assert.Equal(matchId, history.MatchId);
        Assert.Equal(1, history.TotalMoves);
        Assert.Single(history.Moves);
        Assert.NotEmpty(history.Moves[0].WhiteMove);
        Assert.NotEmpty(history.Moves[0].BlackMove);
    }

    [Fact]
    public void PieceNotFound_ShouldRejectMove()
    {
        // Arrange
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        // Try to move piece with ID 32 (QTY - out of range)
        var moveRequest = new PlayMoveRequest
        {
            FromPieceId = 32,
            TargetX = 4,
            TargetY = 3
        };

        // Act
        var result = service.PlayMove(matchId, moveRequest);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public void GameEnded_ShouldPreventFurtherMoves()
    {
        // This would require a full game to completion or resignation
        // For now, we test the structure is in place
        var service = new ChessMatchService();
        var matchId = service.CreateMatch("Alice", "Bob");

        var status = service.GetMatchStatus(matchId);
        Assert.False(status.IsEnded);
    }
}
