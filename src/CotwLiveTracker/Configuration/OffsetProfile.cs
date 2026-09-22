namespace CotwLiveTracker.Configuration;

internal sealed record OffsetProfile(
    string Name,
    string GameBuild,
    string? ExecutableSha256,
    IReadOnlyDictionary<string, string> Offsets);
