using Robust.Shared.GameStates;

namespace Content.Shared.Xenobiology;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSlimeComponent : Component
{
    [DataField, AutoNetworkedField]
    public double Hunger = 90;

    [DataField, AutoNetworkedField]
    public SlimeMood Mood = SlimeMood.Calm;

    [DataField]
    public double HungerDrain = 1.5;

    [DataField]
    public TimeSpan DrainInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public int FullThreshold = 150;

    [DataField]
    public int CalmThreshold = 80;

    [DataField]
    public int AngerThreshold = 40;

    public TimeSpan NextDrain;
}
