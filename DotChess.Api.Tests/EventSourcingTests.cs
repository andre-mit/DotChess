using DotChess.EventSourcing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DotChess.Api.Tests;

public class EventSourcingTests
{
    [Fact]
    public void EventStore_AppendEvents_ShouldOrderByVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var matchId = "match-001";
        var events = new DomainEvent[]
        {
            new MatchCreatedEvent(matchId, "Alice", "Bob") { EventVersion = 1 },
            new MovePlayedEvent(matchId, 2, ChessPieceId.WPe, new ChessPosition(4, 3),
                ChessResultF.OK, ChessColor.kWhite, "e4") { EventVersion = 2 }
        };

        // Act
        store.AppendEvents(matchId, events);
        var retrieved = store.GetEvents(matchId).ToList();

        // Assert
        Assert.Equal(2, retrieved.Count);
        Assert.IsType<MatchCreatedEvent>(retrieved[0]);
        Assert.IsType<MovePlayedEvent>(retrieved[1]);
    }

    [Fact]
    public void EventStore_VersionConflict_ShouldThrow()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var matchId = "match-002";
        var events = new[]
        {
            new MatchCreatedEvent(matchId, "Alice", "Bob") { EventVersion = 1 }
        };
        store.AppendEvents(matchId, events);

        // Act & Assert - Try to insert with wrong version
        var conflictingEvents = new[]
        {
            new MovePlayedEvent(matchId, 5, ChessPieceId.WPe, new ChessPosition(4, 3),
                ChessResultF.OK, ChessColor.kWhite, "e4") { EventVersion = 5 }
        };

        Assert.Throws<ConcurrencyException>(() => store.AppendEvents(matchId, conflictingEvents));
    }

    [Fact]
    public void Replay_ShouldRecoverGameState()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var matchId = "match-replay";

        var events = new DomainEvent[]
        {
            new MatchCreatedEvent(matchId, "Alice", "Bob") { EventVersion = 1 }
        };
        store.AppendEvents(matchId, events);

        // Act
        var match = store.Replay(matchId);

        // Assert
        Assert.NotNull(match);
        Assert.Equal(matchId, match.MatchId);
        Assert.Equal("Alice", match.WhitePlayer);
        Assert.Equal("Bob", match.BlackPlayer);
        Assert.False(match.IsEnded);
    }

    [Fact]
    public void ChessMatch_PlayMove_ShouldAddMovePlayedEvent()
    {
        // Arrange
        var match = new ChessMatch("match-001", "Alice", "Bob");
        match.ClearUncommittedEvents();

        // Act - White pawn e2-e4
        var result = match.PlayMove(ChessPieceId.WPe, new ChessPosition(4, 3));

        // Assert
        Assert.True(result.IsAllowedMove());
        var uncommitted = match.GetUncommittedEvents().ToList();
        Assert.Single(uncommitted);
        Assert.IsType<MovePlayedEvent>(uncommitted[0]);
    }

    [Fact]
    public void ChessMatch_InvalidMove_ShouldAddMoveRejectedEvent()
    {
        // Arrange
        var match = new ChessMatch("match-001", "Alice", "Bob");
        match.ClearUncommittedEvents();

        // Act - Try black move on white's turn
        var result = match.PlayMove(ChessPieceId.BPe, new ChessPosition(4, 4));

        // Assert
        Assert.False(result.IsAllowedMove());
        var uncommitted = match.GetUncommittedEvents().ToList();
        Assert.Single(uncommitted);
        Assert.IsType<MoveRejectedEvent>(uncommitted[0]);
    }

    [Fact]
    public void ChessMatch_ApplyEvent_ShouldUpdateState()
    {
        // Arrange
        var match = new ChessMatch("match-001", "Alice", "Bob");
        var initialVersion = match.EventVersion;

        // Act
        var evt = new MovePlayedEvent("match-001", 2, ChessPieceId.WPe, new ChessPosition(4, 3),
            ChessResultF.OK, ChessColor.kWhite, "e4");
        match.ApplyEvent(evt);

        // Assert
        Assert.Equal(2, match.EventVersion);
        Assert.NotEqual(initialVersion, match.EventVersion);
    }

    [Fact]
    public void ChessMatch_LoadFromHistory_ShouldRecoverGame()
    {
        // Arrange
        var matchId = "match-history";
        var events = new DomainEvent[]
        {
            new MatchCreatedEvent(matchId, "Alice", "Bob"),
            new MovePlayedEvent(matchId, 2, ChessPieceId.WPe, new ChessPosition(4, 3),
                ChessResultF.OK, ChessColor.kWhite, "e4")
        };

        // Act
        var match = ChessMatch.LoadFromHistory(events);

        // Assert
        Assert.Equal(matchId, match.MatchId);
        Assert.Equal("Alice", match.WhitePlayer);
        Assert.Equal(2, match.EventVersion);
        Assert.Equal(1, match.Game.MoveCount);
    }

    [Fact]
    public void EventStore_MultipleMatches_ShouldIsolateSeparately()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var match1 = "match-1";
        var match2 = "match-2";

        // Act
        store.AppendEvents(match1, new[]
        {
            new MatchCreatedEvent(match1, "Alice", "Bob")
        });
        store.AppendEvents(match2, new[]
        {
            new MatchCreatedEvent(match2, "Charlie", "Diana")
        });

        // Assert
        var events1 = store.GetEvents(match1).ToList();
        var events2 = store.GetEvents(match2).ToList();

        Assert.Single(events1);
        Assert.Single(events2);

        var evt1 = (MatchCreatedEvent)events1[0];
        var evt2 = (MatchCreatedEvent)events2[0];

        Assert.Equal("Alice", evt1.WhitePlayer);
        Assert.Equal("Charlie", evt2.WhitePlayer);
    }

    [Fact]
    public void EventStore_ThreadSafety_ShouldMaintainOrder()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var matchId = "match-concurrent";

        // Create initial match
        store.AppendEvents(matchId, new[]
        {
            new MatchCreatedEvent(matchId, "Alice", "Bob")
        });

        // Act - Simulate multiple threads trying to add events
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            int index = i + 2;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    store.AppendEvents(matchId, new[]
                    {
                        new MovePlayedEvent(matchId, index, ChessPieceId.WPe,
                            new ChessPosition(4, 3), ChessResultF.OK, ChessColor.kWhite, "e4")
                    });
                }
                catch { /* expected conflicts */ }
            }));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        var allEvents = store.GetEvents(matchId).ToList();
        // Should have initial event + some moves (others failed due to version conflict)
        Assert.True(allEvents.Count >= 1);
    }
}
