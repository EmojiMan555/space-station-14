using Content.Shared.Xenobiology;
using Lidgren.Network;
using Robust.Shared.Timing;

namespace Content.Server.Xenobiology;

public sealed partial class XenoSlimeHungerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoSlimeComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<XenoSlimeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextDrain = _timing.CurTime + ent.Comp.DrainInterval;
        UpdateMood(ent);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<XenoSlimeComponent>();

        while (query.MoveNext(out var uid, out var slime))
        {
            if (_timing.CurTime < slime.NextDrain)
                continue;

            slime.NextDrain = _timing.CurTime + slime.DrainInterval;
            AddHunger((uid, slime), -slime.HungerDrain);
        }
    }

    public void AddHunger(Entity<XenoSlimeComponent> ent, double amount)
    {
        var newHunger = Math.Max(0, ent.Comp.Hunger + amount);

        if (newHunger == ent.Comp.Hunger)
            return;
        ent.Comp.Hunger = newHunger;
        UpdateMood(ent);
        Dirty(ent);
    }

    private void UpdateMood(Entity<XenoSlimeComponent> ent)
    {
        var c = ent.Comp;

        if (c.Hunger >= c.FullThreshold)
            c.Mood = SlimeMood.Full;
        else if (c.Hunger >= c.CalmThreshold)
            c.Mood = SlimeMood.Calm;
        else if (c.Hunger >= c.AngerThreshold)
            c.Mood = SlimeMood.Angry;
        else
            c.Mood = SlimeMood.Rabid;
    }
}
