using Content.Shared.Xenobiology;

namespace Content.Shared.Xenobiology.Slime;

[ByRefEvent]
public record struct XenoSlimeHungerChangedEvent(SlimeMood OldMood);
