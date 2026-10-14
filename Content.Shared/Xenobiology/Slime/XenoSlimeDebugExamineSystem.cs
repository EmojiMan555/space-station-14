using Content.Shared.Examine;        // where ExaminedEvent lives

namespace Content.Shared.Xenobiology.Slime;

// A small separate system, just for debug text. Easy to delete later.
public sealed class XenoSlimeDebugExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // "When an entity WITH XenoSlimeComponent is examined, run OnExamined"
        SubscribeLocalEvent<XenoSlimeComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<XenoSlimeComponent> ent, ref ExaminedEvent args)
    {
        // Pick a text color for each mood.
        // A normal switch works here because Full/Calm/Angry/Rabid are FIXED values (constants),
        // unlike the hunger thresholds, which are variables.
        var color = ent.Comp.Mood switch
        {
            SlimeMood.Full  => "lime",
            SlimeMood.Calm  => "green",
            SlimeMood.Angry => "orange",
            SlimeMood.Rabid => "red",
            _               => "white",   // safety net, in case we add a new mood later
        };

        var c = ent.Comp;
        var size = 1f;
        if (c.Hunger >= c.BigHungerThreshold)
            size += c.HungerSizeBonus;

        size += c.MergeCount * c.MergeSizeBonus;

        // PushMarkup = add one line to the examine box.
        // $"..." = "string interpolation": anything inside { } gets replaced by its value.
        //          So {ent.Comp.Hunger} becomes something like 87.
        // [color=red]...[/color] = SS14 text markup that colors the words between the tags.
        args.PushMarkup($"[color={color}]Mood: {ent.Comp.Mood}[/color]");
        args.PushMarkup($"Hunger: {ent.Comp.Hunger} / {ent.Comp.FullThreshold}");
        args.PushMarkup($"Merge: {ent.Comp.MergeCount}");
        args.PushMarkup($"Size: {size:F1}");
    }
}
