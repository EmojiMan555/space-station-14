using Content.Shared.Mobs.Systems;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Xenobiology;

/// <summary>
/// Step 8: same-color slimes that touch become one bigger slime (spec 11).
/// Slimes never walk to each other for this (spec 11.2): we only check who is
/// already touching, every 2 seconds, with a tiny search radius.
/// </summary>
public sealed partial class XenoSlimeMergeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private XenoSlimeHungerSystem _hunger = default!;
    [Dependency] private XenoSlimeVisualsSystem _visuals = default!;

    private readonly HashSet<Entity<XenoSlimeComponent>> _nearby = new();

    // Slimes already merged this tick. QueueDel only deletes at the END of the tick,
    // so without this list a deleted slime could merge a second time.
    private readonly HashSet<EntityUid> _mergedThisTick = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        _mergedThisTick.Clear();

        var query = EntityQueryEnumerator<XenoSlimeComponent>();

        while (query.MoveNext(out var uid, out var slime))
        {
            // First time we see this slime: pick a random first check time (spreads the work)
            if (slime.NextMergeCheck == TimeSpan.Zero)
            {
                slime.NextMergeCheck = now + slime.MergeCheckInterval * _random.NextFloat();
                continue;
            }

            if (now < slime.NextMergeCheck)
                continue;

            slime.NextMergeCheck = now + slime.MergeCheckInterval;

            if (_mergedThisTick.Contains(uid))
                continue;

            TryMergeWithNeighbour((uid, slime));
        }
    }

    private void TryMergeWithNeighbour(Entity<XenoSlimeComponent> ent)
    {
        if (!CanMerge(ent))
            return;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.MergeTouchRange, _nearby);

        foreach (var other in _nearby)
        {
            if (other.Owner == ent.Owner || _mergedThisTick.Contains(other.Owner))
                continue;

            if (!IsSameColor(ent, other) || !CanMerge(other))
                continue;

            // Result can't go over 4 merges (spec 11.5 + 11.6)
            if (ent.Comp.MergeCount + other.Comp.MergeCount + 1 > ent.Comp.MaxMergeCount)
                continue;

            Merge(ent, other);
            return;     // one merge per check is enough
        }
    }

    // Spec 11.1.3: both Calm or Full, alive, not splitting, not eating
    private bool CanMerge(Entity<XenoSlimeComponent> ent)
    {
        if (ent.Comp.Mood is not (SlimeMood.Calm or SlimeMood.Full))
            return false;

        if (ent.Comp.MergeCount >= ent.Comp.MaxMergeCount)
            return false;

        // Just born from a split? Not yet.
        if (_timing.CurTime < ent.Comp.NoMergeUntil)
            return false;

        if (!_mobState.IsAlive(ent) || _container.IsEntityInContainer(ent))
            return false;

        if (HasComp<XenoSlimeSplittingComponent>(ent))
            return false;

        if (TryComp<XenoSlimeBrainComponent>(ent, out var brain)
            && brain.State == XenoSlimeBrainState.EatingCorpse)
            return false;

        return true;
    }

    // Same color = same prototype ID (spec 11.1.1)
    private bool IsSameColor(EntityUid a, EntityUid b)
    {
        var protoA = MetaData(a).EntityPrototype?.ID;
        var protoB = MetaData(b).EntityPrototype?.ID;
        return protoA != null && protoA == protoB;
    }

    private void Merge(Entity<XenoSlimeComponent> keep, Entity<XenoSlimeComponent> eaten)
    {
        var k = keep.Comp;
        var e = eaten.Comp;

        _mergedThisTick.Add(keep);
        _mergedThisTick.Add(eaten);

        k.MergeCount = k.MergeCount + e.MergeCount + 1;             // spec 11.4.4
        k.MutationChance = (k.MutationChance + e.MutationChance) / 2; // spec 11.4.2
        k.Friends.UnionWith(e.Friends);                              // spec 11.4.3 (UnionWith = add all)

        // Average hunger (spec 11.4.1). SetHunger fires the normal events.
        _hunger.SetHunger(keep, (k.Hunger + e.Hunger) / 2);

        // MergeCount changed, so update size even if hunger didn't change (spec 11.4.5)
        _visuals.UpdateSize(keep);

        QueueDel(eaten);
    }
}
