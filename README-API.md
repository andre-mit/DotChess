# Chess Match API

A RESTful API for creating and playing chess matches with **Event Sourcing** architecture.

## Quick Start

### 1. Run the API

```bash
dotnet run --project DotChess.Api/DotChess.Api.csproj -c Release
```

The API starts on `http://localhost:5000` (or `http://localhost:5080` if 5000 is in use). Swagger UI is available at `/swagger`.

### 2. Create a Match

```bash
curl -X POST http://localhost:5000/api/matches \
  -H "Content-Type: application/json" \
  -d '{"whitePlayer":"Alice","blackPlayer":"Bob"}'
```

Response:
```json
{
  "id":"550e8400-e29b-41d4-a716-446655440000",
  "whitePlayer":"Alice",
  "blackPlayer":"Bob",
  "status":"InProgress",
  "turn":"White",
  "isCheck":false,
  "isFinished":false,
  "winner":null,
  "fen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
  "moveCount":0,
  "version":1
}
```

### 3. Play Moves

```bash
MATCH_ID="550e8400-e29b-41d4-a716-446655440000"

# White's move
curl -X POST http://localhost:5000/api/matches/$MATCH_ID/moves \
  -H "Content-Type: application/json" \
  -d '{"from":"e2","to":"e4","promoteToKnight":false}'

# Black's move
curl -X POST http://localhost:5000/api/matches/$MATCH_ID/moves \
  -H "Content-Type: application/json" \
  -d '{"from":"e7","to":"e5","promoteToKnight":false}'
```

### 4. Check Status

```bash
curl http://localhost:5000/api/matches/$MATCH_ID
```

Response shows current FEN, whose turn it is, check/checkmate/stalemate state, and version (for optimistic concurrency).

### 5. Get Move History

```bash
curl http://localhost:5000/api/matches/$MATCH_ID/history
```

Response:
```json
[
  {
    "ply":1,"version":2,"color":"White","from":"e2","to":"e4","promoteToKnight":false,
    "san":"e4","fenAfter":"rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
    "occurredAt":"2026-01-01T00:00:00Z"
  },
  {
    "ply":2,"version":3,"color":"Black","from":"e7","to":"e5","promoteToKnight":false,
    "san":"e5","fenAfter":"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
    "occurredAt":"2026-01-01T00:00:00Z"
  }
]
```

### 6. Export to PGN

```bash
curl http://localhost:5000/api/matches/$MATCH_ID/pgn
```

Response (Portable Game Notation):
```
[Event "Chess Match"]
[Site "??"]
[Date "????.??.??"]
[Round "?"]
[White "Alice"]
[Black "Bob"]
[Result "*"]
[FEN "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2"]

1. e4 e5 2. Nf3 *
```

## API Endpoints

| Method | Path | Body | Response |
|--------|------|------|----------|
| **POST** | `/api/matches` | `{whitePlayer?, blackPlayer?}` | `201 Created` + match view |
| **POST** | `/api/matches/{id}/moves` | `{from, to, promoteToKnight?}` | `200 OK` + match view or `422 Unprocessable Entity` |
| **GET** | `/api/matches/{id}` | — | `200 OK` + match view or `404 Not Found` |
| **GET** | `/api/matches/{id}/history` | — | `200 OK` + array of moves or `404 Not Found` |
| **GET** | `/api/matches/{id}/pgn` | — | `200 OK` + text/pgn or `404 Not Found` |

### Response Codes

- **201 Created** — Match created successfully.
- **200 OK** — Request succeeded.
- **400 Bad Request** — Invalid input (malformed JSON, bad match ID format).
- **404 Not Found** — Match ID doesn't exist.
- **409 Conflict** — Concurrency conflict: the match has been modified since you loaded it. Reload and retry.
- **422 Unprocessable Entity** — Illegal move (e.g., off-turn player, moving non-existent piece, putting own king in check, or match already finished).

### Error Response

```json
{
  "status": 422,
  "title": "Chess API error",
  "detail": "It is White's turn; e7-e8 holds a Black piece."
}
```

## Standard Algebraic Notation (SAN)

Moves are recorded in SAN format:

| Move Type | Example |
|-----------|---------|
| Pawn move | `e4`, `exd5` (capture), `e8=Q` (promotion) |
| Piece move | `Nf3`, `Bc4`, `Rh1-h8` |
| Capture | `Nxe4`, `Bxc6` |
| Castling | `O-O` (kingside), `O-O-O` (queenside) |
| Check/Mate | `Nf3+`, `Qh5#` |

## Pawn Promotion

Promote a pawn at the last rank:

```bash
curl -X POST http://localhost:5000/api/matches/$MATCH_ID/moves \
  -H "Content-Type: application/json" \
  -d '{"from":"e7","to":"e8","promoteToKnight":false}'
  # false = Queen (default), true = Knight
```

The response SAN will show `e8=Q` or `e8=N` respectively.

## Optimistic Concurrency

Each match has a `version` number that increases with every move. If you try to submit a move based on an outdated version, the write fails with **409 Conflict**. The match state includes the current version; always check it before retrying:

1. Load match: `GET /api/matches/{id}` → `version: 5`
2. User makes move locally
3. Try to submit: `POST /api/matches/{id}/moves` with the move
4. If another client moved in the meantime, you get `409` and actual `version: 6`
5. Reload and retry the move on the new version

## Architecture

- **DotChess**: Core rules engine (netstandard2.1). Validates moves, detects check/checkmate, handles castling and en passant.
- **DotChess.EventSourcing** (netstandard2.1): Event Sourcing domain. `ChessMatch` aggregate, in-memory event store, replay and consistency verification.
- **DotChess.Api** (.NET 10): Minimal APIs. HTTP endpoints, error mapping, Swagger UI.

### Event Store

Events are stored in a stream per match (`match-<uuid>`). Each event has a version (1, 2, 3, ...).

- `MatchCreated`: Initial event when a match is created.
- `MovePlayed`: Recorded after a legal move. Contains the move (from/to), result (check/checkmate/etc.), SAN notation, and the full FEN after the move.

Replay verifies that every stored move produces the exact same SAN, FEN, and result. If a move diverges, replay fails (indicating storage corruption).

## Running Tests

```bash
dotnet test DotChess.Api.Tests/DotChess.Api.Tests.csproj -c Release
```

Tests cover:

- Happy path: creating a match, playing moves, checking status and history.
- Invalid moves: rejected without recording events.
- Out-of-turn moves: rejected.
- Concurrency: two writers from the same version; second write fails without lost updates.
- Replay: corrupted history (wrong SAN/FEN) is detected.
- PGN generation and consistency.

## Known Limitations

- **In-memory store**: Events are lost on restart. To persist, replace `IEventStore` registration with a durable implementation.
- **No authentication**: Any caller can move for either side. The engine enforces whose turn it is, not player identity.
- **No time controls**: No clock; players can think forever.
- **No resignation/draw**: Only checkmate and stalemate end the game.
- **Single instance**: The store is not shared across multiple API instances. Run one instance per store.

## Future Enhancements

- PostgreSQL or SQLite event store.
- WebSocket notifications for live games.
- Resignation and draw offers.
- Time controls (blitz, rapid, classical).
- Snapshots to speed up long games (1000+ moves).
- CQRS read models for fast queries (e.g., leaderboards).
- Authentication and authorization.
- Elo rating and matchmaking.
