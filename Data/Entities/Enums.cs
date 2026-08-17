namespace oyinQ.Bot.Data.Entities;

public enum GameCopySource
{
    Personal = 0,
    Club = 1
}

public enum BringStatus
{
    Bringing = 0,
    Maybe = 1
}

public enum SessionStatus
{
    Recruiting = 0,
    Full = 1,
    Closed = 2
}

public enum ImportStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum ImportTarget
{
    Participant = 0,
    Club = 1
}

public enum ExternalGameProvider
{
    Bgg = 0,
    Tesera = 1
}
