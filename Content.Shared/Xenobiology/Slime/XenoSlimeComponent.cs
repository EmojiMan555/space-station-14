using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Xenobiology.Slime;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSlimeComponent : Component
{
    // ---------------- Hunger and mood (Steps 1 + 2.5) ----------------

    // Server-only: no AutoNetworkedField, so it is never sent to clients.
    [DataField]
    public double Hunger = 90;

    // Networked: changes rarely, and the client may need it for visuals later.
    [DataField, AutoNetworkedField]
    public SlimeMood Mood = SlimeMood.Calm;

    [DataField]
    public double HungerDrain = 1;

    [DataField]
    public TimeSpan DrainInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public int FullThreshold = 150;

    [DataField]
    public int CalmThreshold = 80;

    [DataField]
    public int AngerThreshold = 40;

    public TimeSpan NextDrain;

    // ---------------- Size (Step 2) ----------------

    [DataField]
    public int BigHungerThreshold = 120;

    [DataField]
    public float HungerSizeBonus = 0.3f;

    [DataField]
    public float MergeSizeBonus = 0.1f;

    // ---------------- Splitting + mutation (Step 6) ----------------

    // Chance (0 to 1) that a baby becomes a new color. 0.15 = 15% (spec 12.2)
    [DataField]
    public float MutationChance = 0.15f;

    // How much each split adds to the babies' chance (spec 12.3)
    [DataField]
    public float MutationChancePerSplit = 0.15f;

    // Colors this slime's babies can mutate into (spec 12.6). Empty = final color.
    // EntProtoId = "the ID of an entity prototype", checked when YAML loads.
    [DataField]
    public List<EntProtoId> Mutations = new();

    // What a baby becomes when its chance reaches 100% (spec 12.5)
    [DataField]
    public EntProtoId RainbowPrototype = "MobXenoSlimeRainbow";

    [DataField]
    public TimeSpan SplitShakeTime = TimeSpan.FromSeconds(3);

    // Every baby starts at this hunger (spec 10.4)
    [DataField]
    public double BabyHunger = 90;

    // ---------------- Feeding + corpses (Step 7) ----------------

    // Hunger gained per hand-feed bite (spec 9.4)
    [DataField]
    public double FeedHunger = 5;

    // Hunger gained per corpse bite (spec 9.2.2)
    [DataField]
    public double CorpseBiteHunger = 5;

    // Bites until a corpse is gone (spec 9.2.3)
    [DataField]
    public int CorpseBitesToFinish = 20;

    // ---------------- Merging (Step 8) ----------------

    [DataField]
    public int MergeCount = 0;

    [DataField]
    public int MaxMergeCount = 4;              // max 5 slimes in one (spec 11.5)

    // How close two slimes must be (center to center) to count as "touching"
    [DataField]
    public float MergeTouchRange = 0.7f;

    [DataField]
    public TimeSpan MergeCheckInterval = TimeSpan.FromSeconds(2);

    public TimeSpan NextMergeCheck;

    // Babies spawn touching each other, Calm and the same color, so without this
    // they would merge right back together a second after splitting.
    // (default, tune later — not in the spec)
    [DataField]
    public TimeSpan MergeCooldownAfterSplit = TimeSpan.FromSeconds(30);

    public TimeSpan NoMergeUntil;

    // ---------------- Friends (Step 9) ----------------

    // Entities this slime considers friends (spec 5.3). Server-only memory.
    public HashSet<EntityUid> Friends = new();

    [DataField]
    public float FriendChanceCalm = 0.3f;      // spec 5.2

    [DataField]
    public float FriendChanceAngry = 0.1f;

    // Rabid is always 0% (spec 5.2), so it has no setting.
}
