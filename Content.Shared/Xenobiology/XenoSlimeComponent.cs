using Robust.Shared.GameStates;

namespace Content.Shared.Xenobiology;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSlimeComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Hunger = 90;

    [DataField, AutoNetworkedField]
    public SlimeMood Mood = SlimeMood.Calm;

    [DataField]
    public int HungerDrain = 1;

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
