using System.Numerics;
using Content.Shared.Sprite;
using Content.Shared.Xenobiology.Slime;

namespace Content.Server.Xenobiology;

public sealed partial class XenoSlimeVisualsSystem : EntitySystem
{
    [Dependency] private SharedScaleVisualsSystem _scale = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoSlimeComponent, XenoSlimeHungerChangedEvent>(OnHungerChanged);
    }

    private void OnHungerChanged(Entity<XenoSlimeComponent> ent, ref XenoSlimeHungerChangedEvent args)
    {
        UpdateSize(ent);
    }

    public void UpdateSize(Entity<XenoSlimeComponent> ent)
    {
        var c = ent.Comp;
        var size = 1f;
        if (c.Hunger >= c.BigHungerThreshold)
            size += c.HungerSizeBonus;

        size += c.MergeCount * c.MergeSizeBonus;

        var scale = new Vector2(size, size);

        if (_scale.GetSpriteScale(ent.Owner) != scale)
            _scale.SetSpriteScale(ent.Owner, scale);
    }
}
