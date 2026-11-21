using DotChess.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<ChessMatchService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var service = app.Services.GetRequiredService<ChessMatchService>();

// POST /api/matches - Create new match
app.MapPost("/api/matches", (CreateMatchRequest req) =>
{
    var matchId = service.CreateMatch(req.WhitePlayer, req.BlackPlayer);
    return Results.Created($"/api/matches/{matchId}", new { matchId });
})
.WithName("CreateMatch")
.WithOpenApi();

// POST /api/matches/{matchId}/moves - Play a move
app.MapPost("/api/matches/{matchId}/moves", (string matchId, PlayMoveRequest req) =>
{
    var result = service.PlayMove(matchId, req);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
})
.WithName("PlayMove")
.WithOpenApi();

// GET /api/matches/{matchId} - Get current match status
app.MapGet("/api/matches/{matchId}", (string matchId) =>
{
    var status = service.GetMatchStatus(matchId);
    return status != null ? Results.Ok(status) : Results.NotFound();
})
.WithName("GetMatchStatus")
.WithOpenApi();

// GET /api/matches/{matchId}/history - Get match history
app.MapGet("/api/matches/{matchId}/history", (string matchId) =>
{
    var history = service.GetMatchHistory(matchId);
    return history != null ? Results.Ok(history) : Results.NotFound();
})
.WithName("GetMatchHistory")
.WithOpenApi();

app.Run();
