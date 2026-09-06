namespace HimeMikotoDesktopNative;

internal sealed record DanceMotionDefinition(
    string Key,
    string ChineseName,
    string EnglishName,
    string RelativePath,
    string? SecondaryRelativePath = null,
    bool Loop = false,
    string? UsageNote = null,
    string? UnavailableNote = null,
    int StartFrame = 0)
{
    internal bool IsDual => !string.IsNullOrWhiteSpace(SecondaryRelativePath);
}
