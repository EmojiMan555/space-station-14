using Robust.Shared.Timing;
using Content.Shared.Xenobiology.Slime;

namespace Content.Server.Xenobiology;

public sealed partial class XenoSlimeHungerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

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
        Dirty(ent);
        RaiseHungerChanged(ent, oldMood);

        // Always announce the starting mood once, so a slime that SPAWNS Full
        // (hunger 150+) still splits.
        var moodEv = new XenoSlimeMoodChangedEvent(oldMood, ent.Comp.Mood);
        RaiseLocalEvent(ent.Owner, ref moodEv);
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

        // 80 is Angry and 40 is Rabid (spec 3.2-3.4)
        if (c.Hunger >= c.FullThreshold)        // 150 or more
            c.Mood = SlimeMood.Full;
        else if (c.Hunger > c.CalmThreshold)    // more than 80
            c.Mood = SlimeMood.Calm;
        else if (c.Hunger > c.AngerThreshold)   // more than 40
            c.Mood = SlimeMood.Angry;
        else                                    // 40 or less
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

        // Only network when the mood changed (Step 2.5)
        if (ent.Comp.Mood != oldMood)
            Dirty(ent);

        RaiseHungerChanged(ent, oldMood);

        // NEW (Step 6): a second, rarer event only when the mood changes
        if (ent.Comp.Mood != oldMood)
        {
            var moodEv = new XenoSlimeMoodChangedEvent(oldMood, ent.Comp.Mood);
            RaiseLocalEvent(ent.Owner, ref moodEv);
        }
    }

    /// <summary>
    /// Sets hunger to an exact value (used by merging: "hunger = average of both").
    /// It just works out the difference and reuses AddHunger, so all events still fire.
    /// </summary>
    public void SetHunger(Entity<XenoSlimeComponent> ent, double value)
    {
        AddHunger(ent, value - ent.Comp.Hunger);
    }

    private void RaiseHungerChanged(Entity<XenoSlimeComponent> ent, SlimeMood oldMood)
    {
        var ev = new XenoSlimeHungerChangedEvent(oldMood);
        RaiseLocalEvent(ent.Owner, ref ev);
    }
}
