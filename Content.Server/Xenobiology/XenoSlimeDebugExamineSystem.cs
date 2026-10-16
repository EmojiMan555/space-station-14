using Content.Shared.Examine;
using Content.Shared.Xenobiology.Slime;

namespace Content.Server.Xenobiology;

// Server-side examine: your client asks the server for examine text,
// and the server (which has the REAL hunger) runs this.
public sealed partial class XenoSlimeDebugExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoSlimeComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<XenoSlimeComponent> ent, ref ExaminedEvent args)
    {
        var c = ent.Comp;

        var color = c.Mood switch
        {
            SlimeMood.Full  => "lime",
            SlimeMood.Calm  => "green",
            SlimeMood.Angry => "orange",
            SlimeMood.Rabid => "red",
            _               => "white",
        };

        args.PushMarkup($"[color={color}]Mood: {c.Mood}[/color]");
        args.PushMarkup($"Hunger: {c.Hunger:0.0} / {c.FullThreshold}");
        args.PushMarkup($"Merges: {c.MergeCount} / {c.MaxMergeCount}");

        // :0% = show a 0-1 number as a percentage, e.g. 0.3 → "30%"
        args.PushMarkup($"Mutation chance: {c.MutationChance:0%}");

        // Contains = "is this entity in the set?"
        var friend = c.Friends.Contains(args.Examiner) ? " (you are one)" : "";
        args.PushMarkup($"Friends: {c.Friends.Count}{friend}");

        if (TryComp<XenoSlimeBrainComponent>(ent, out var brain))
            args.PushMarkup($"Brain: {brain.State}");
    }
}
