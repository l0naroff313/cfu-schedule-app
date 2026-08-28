namespace UniversitySchedule.Contracts.System;

public sealed record SystemHealthResponse(
    string Status,
    DateTimeOffset ServerTimeUtc);
