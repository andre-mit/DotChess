using System;

namespace DotChess.Api;

public class CreateMatchRequest
{
    public string WhitePlayer { get; set; }
    public string BlackPlayer { get; set; }
}

public class PlayMoveRequest
{
    public int FromPieceId { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public bool PromoteToKnight { get; set; }
}

public class MatchStatusResponse
{
    public string MatchId { get; set; }
    public string WhitePlayer { get; set; }
    public string BlackPlayer { get; set; }
    public int MoveCount { get; set; }
    public string CurrentTurn { get; set; }
    public string FEN { get; set; }
    public bool IsEnded { get; set; }
    public string Winner { get; set; }
    public string[] ValidMoves { get; set; }
}

public class MoveResponse
{
    public bool Success { get; set; }
    public string MoveNotation { get; set; }
    public string Result { get; set; }
    public string ErrorMessage { get; set; }
    public int CurrentVersion { get; set; }
}

public class MatchHistoryResponse
{
    public string MatchId { get; set; }
    public int TotalMoves { get; set; }
    public MoveHistoryItem[] Moves { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MoveHistoryItem
{
    public int MoveNumber { get; set; }
    public string WhiteMove { get; set; }
    public string BlackMove { get; set; }
    public string FENAfterMove { get; set; }
}
