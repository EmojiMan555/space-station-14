using Robust.Shared.Timing;
using Content.Shared.Xenobiology.Slime;


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

        var oldMood = ent.Comp.Mood;
        UpdateMood(ent);
        RaiseHungerChanged(ent, oldMood);
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

    public void AddHunger(Entity<XenoSlimeComponent> ent, double amount)
    {
        var newHunger = Math.Max(0, ent.Comp.Hunger + amount);

        if (newHunger == ent.Comp.Hunger)
            return;

        var oldMood = ent.Comp.Mood;
        ent.Comp.Hunger = newHunger;
        UpdateMood(ent);
        Dirty(ent);
        RaiseHungerChanged(ent, oldMood);
    }

    private void RaiseHungerChanged(Entity<XenoSlimeComponent> ent, SlimeMood oldMood)
    {
        var ev = new XenoSlimeHungerChangedEvent(oldMood);
        RaiseLocalEvent(ent.Owner, ref ev);
    }
}
