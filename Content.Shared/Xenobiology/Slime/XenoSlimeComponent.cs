using Robust.Shared.GameStates;

namespace Content.Shared.Xenobiology.Slime;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSlimeComponent : Component
{
    //merge
    [DataField]
    public int MergeCount = 0;

    //mood/hunger
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

    //appearance
    [DataField]
    public int BigHungerThreshold = 120;

    [DataField]
    public float HungerSizeBonus = 0.3f;

    [DataField]
    public float MergeSizeBonus = 0.1f;
}
