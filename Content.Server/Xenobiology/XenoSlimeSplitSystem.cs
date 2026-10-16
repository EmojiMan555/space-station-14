using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Xenobiology;

/// <summary>
/// Step 6: splitting (spec 10) and mutation (spec 12).
/// Works for AI slimes AND player-controlled slimes, because it only reacts to mood.
/// </summary>
public sealed partial class XenoSlimeSplitSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private XenoSlimeVisualsSystem _visuals = default!;
    [Dependency] private XenoSlimeHungerSystem _hunger = default!;

    // Slimes that are ready to split this tick. We can't split INSIDE the query loop:
    // spawning babies adds new XenoSlimeComponents to the list we're walking through,
    // and C# throws "Collection was modified" (found this in testing!).
    private readonly List<Entity<XenoSlimeComponent>> _toSplit = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoSlimeComponent, XenoSlimeMoodChangedEvent>(OnMoodChanged);
    }

    private void OnMoodChanged(Entity<XenoSlimeComponent> ent, ref XenoSlimeMoodChangedEvent args)
    {
        // Only care about "just became Full" (spec 10.1)
        if (args.NewMood != SlimeMood.Full)
            return;

        if (HasComp<XenoSlimeSplittingComponent>(ent) || !_mobState.IsAlive(ent))
            return;

        var splitting = AddComp<XenoSlimeSplittingComponent>(ent);
        splitting.SplitAt = _timing.CurTime + ent.Comp.SplitShakeTime;

        // Vanilla shaking effect (spec 10.2). refresh: true = restart the timer if already shaking
        _jitter.DoJitter(ent, ent.Comp.SplitShakeTime, refresh: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        _toSplit.Clear();

        // Only slimes that are shaking have XenoSlimeSplittingComponent, so this is tiny
        var query = EntityQueryEnumerator<XenoSlimeSplittingComponent, XenoSlimeComponent>();

        while (query.MoveNext(out var uid, out var splitting, out var slime))
        {
            if (now < splitting.SplitAt)
                continue;

            // Died while shaking? Cancel. RemCompDeferred = remove it safely after this loop.
            if (!_mobState.IsAlive(uid))
            {
                RemCompDeferred<XenoSlimeSplittingComponent>(uid);
                continue;
            }

            _toSplit.Add((uid, slime));     // remember it, split AFTER the loop
        }

        // Now it's safe to spawn babies
        foreach (var slime in _toSplit)
            Split(slime);
    }

    private void Split(Entity<XenoSlimeComponent> parent)
    {
        var p = parent.Comp;

        // Which prototype is the parent? (MobXenoSlimeGrey, MobXenoSlimeOrange...)
        // ?. = "if EntityPrototype is null, give null instead of crashing"
        var parentProto = MetaData(parent).EntityPrototype?.ID ?? "MobXenoSlimeGrey";

        // Every split adds +15%, capped at 100% (spec 12.3)
        var babyChance = Math.Min(1f, p.MutationChance + p.MutationChancePerSplit);

        // 2 or 3 babies, 50/50 (spec 10.3)
        var count = _random.Prob(0.5f) ? 2 : 3;

        // GetMoverCoordinates = where it really is, even if it's inside something
        var coords = _transform.GetMoverCoordinates(parent);

        for (var i = 0; i < count; i++)
        {
            var proto = PickBabyPrototype(p, parentProto, babyChance);

            // Spread the babies out a little: up to 0.3 tiles in a random direction
            var baby = Spawn(proto, coords.Offset(_random.NextVector2(0.3f)));

            if (!TryComp<XenoSlimeComponent>(baby, out var b))
                continue;

            // Set it explicitly instead of trusting the prototype's default (spec 10.4)
            _hunger.SetHunger((baby, b), p.BabyHunger);

            b.MutationChance = babyChance;
            b.NoMergeUntil = _timing.CurTime + b.MergeCooldownAfterSplit;
            b.MergeCount = Math.Max(0, p.MergeCount - 1);   // spec 11.7 (every baby)
            _visuals.UpdateSize((baby, b));
        }

        QueueDel(parent);
    }

    // Each baby rolls on its own (spec 10.5 + 12.4)
    private EntProtoId PickBabyPrototype(XenoSlimeComponent parent, string parentProto, float babyChance)
    {
        // Reached 100%: Rainbow (spec 12.5)
        if (babyChance >= 1f)
            return parent.RainbowPrototype;

        // Final colors have no mutations: always the same color
        if (parent.Mutations.Count == 0)
            return parentProto;

        // Prob(0.3) = true 30% of the time
        if (_random.Prob(babyChance))
            return _random.Pick(parent.Mutations);   // every target equal chance (spec 12.4)

        return parentProto;
    }
}
