namespace Content.Server.Xenobiology;

/// <summary>
/// Added while a slime is shaking before it splits. Removed when it splits
/// (the slime is deleted). Only shaking slimes have it, so the split system's
/// Update only loops over a handful of entities.
/// </summary>
[RegisterComponent]
public sealed partial class XenoSlimeSplittingComponent : Component
{
    public TimeSpan SplitAt;
}
