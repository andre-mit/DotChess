# DotChess

An open-source chess rules engine and Event Sourcing API.

## Directories

- **DotChess/** — Core rules engine (`netstandard2.1`). Validates moves, checks, castling, en passant, stalemate, 50-move rule, pawn promotion.
- **DotChess.EventSourcing/** — Domain aggregates and event store (`netstandard2.1`). `ChessMatch` as an aggregate root with commands, events, in-memory replay, and optimistic concurrency.
- **DotChess.Api/** — HTTP API (.NET 10). Minimal APIs for creating matches, playing moves, checking status, retrieving history, and exporting to PGN.
- **DotChess.Api.Tests/** — Integration tests (.NET 10, MSTest). Tests the full stack: domain logic, event sourcing, HTTP endpoints.

## Quick Start

### Build

```bash
# Build all projects
dotnet build DotChess16.sln -c Release -p:GeneratePackageOnBuild=false

# Run tests
dotnet test DotChess.Api.Tests/DotChess.Api.Tests.csproj -c Release
```

### Run the API

```bash
dotnet run --project DotChess.Api/DotChess.Api.csproj -c Release
# Open http://localhost:5000/swagger for interactive docs
```

## API Usage

See [README-API.md](README-API.md) for comprehensive examples.

### Create a Match

```bash
curl -X POST http://localhost:5000/api/matches \
  -H "Content-Type: application/json" \
  -d '{"whitePlayer":"Alice","blackPlayer":"Bob"}'
```

### Play a Move

```bash
curl -X POST http://localhost:5000/api/matches/{id}/moves \
  -H "Content-Type: application/json" \
  -d '{"from":"e2","to":"e4"}'
```

### Get Status

```bash
curl http://localhost:5000/api/matches/{id}
```

### Get History

```bash
curl http://localhost:5000/api/matches/{id}/history
```

### Export to PGN

```bash
curl http://localhost:5000/api/matches/{id}/pgn
```

## Architecture

### Domain Model (Event Sourcing)

A `ChessMatch` is an aggregate root that:

1. **Accepts commands** (`CreateMatchCommand`, `PlayMoveCommand`) and validates them.
2. **Raises events** (`MatchCreated`, `MovePlayed`) only if the command is valid.
3. **Applies events** to rebuild its state.
4. **Replays events** to verify consistency: if a stored move diverges from the rules, an exception is raised.

### Event Store

Stores events in streams (one per match). Writes use optimistic concurrency: a write fails if the stream version doesn't match the expected version.

```
Stream: match-{uuid}
Version 1: MatchCreated(whitePlayer, blackPlayer)
Version 2: MovePlayed(ply=1, color=White, from=e2, to=e4, san="e4", ...)
Version 3: MovePlayed(ply=2, color=Black, from=e7, to=e5, san="e5", ...)
...
```

### HTTP API

Endpoints map to domain operations and handle errors:

- `400` — Invalid input.
- `404` — Match not found.
- `409` — Concurrency conflict (version mismatch).
- `422` — Illegal move or finished match.

## Development

### Add a new move type or rule

1. Modify `ChessGame.Move()` or the board validation in `DotChess`.
2. The aggregate will automatically use the new logic during replay.
3. Add tests in `DotChess.Api.Tests`.

### Persist events to a database

1. Implement `IEventStore` (e.g., with PostgreSQL or SQLite).
2. Replace the `InMemoryEventStore` registration in `DotChess.Api/Program.cs`.
3. No other code changes needed.

## Testing

```bash
dotnet test DotChess.Api.Tests/ -c Release --logger "console;verbosity=detailed"
```

Tests include:

- **Happy path**: Creating matches, playing legal moves, verifying status and history.
- **Validation**: Rejecting illegal moves without recording events.
- **Concurrency**: Two writers from the same version; second write fails safely.
- **Replay**: Corrupted history is detected.
- **PGN export**: Headers, moves, and game result.

## License

MIT. See [LICENSE](LICENSE).

## References

- [PGN Specification](https://www.chess.com/terms/chess-notation)
- [FEN (Forsyth–Edwards Notation)](https://en.wikipedia.org/wiki/Forsyth%E2%80%93Edwards_Notation)
- [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)
- [DDD Aggregate Root](https://martinfowler.com/bliki/DDD_Aggregate.html)
