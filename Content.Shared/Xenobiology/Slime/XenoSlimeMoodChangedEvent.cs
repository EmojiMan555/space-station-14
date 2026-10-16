namespace Content.Shared.Xenobiology.Slime;

/// <summary>
/// Raised on a slime ONLY when its mood changes (Calm → Angry, Angry → Full...).
/// Hunger changes every 2 seconds, mood only a few times a minute,
/// so listening to this is much cheaper than listening to every hunger change.
/// </summary>
[ByRefEvent]
public record struct XenoSlimeMoodChangedEvent(SlimeMood OldMood, SlimeMood NewMood);
