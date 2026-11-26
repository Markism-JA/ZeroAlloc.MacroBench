namespace MacroBench.Pacing;

/// <summary>
/// Load pacing strategy for <see cref="OpenLoopPacer"/>.
/// </summary>
public enum PacerMode : byte
{
    /// <summary>Dispatches at maximum hardware rate with no artificial pacing delay.</summary>
    Saturated = 0,

    /// <summary>Dispatches on a fixed open-loop schedule, returning scheduled timestamps to avoid coordinated omission.</summary>
    Paced = 1,
}
