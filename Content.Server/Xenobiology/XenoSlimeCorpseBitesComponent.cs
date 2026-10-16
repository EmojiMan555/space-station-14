namespace Content.Server.Xenobiology;

/// <summary>
/// Added to a dead body the first time a slime bites it. Counts the bites.
/// </summary>
[RegisterComponent]
public sealed partial class XenoSlimeCorpseBitesComponent : Component
{
    public int Bites;
}
