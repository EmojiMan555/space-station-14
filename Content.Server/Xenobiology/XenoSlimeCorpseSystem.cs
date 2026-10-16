using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Containers;

namespace Content.Server.Xenobiology;

/// <summary>
/// Step 7: eating dead bodies (spec 9.1-9.2) and hand feeding (spec 9.4).
/// The BRAIN decides when to walk and bite. This system only knows the RULES of eating.
/// </summary>
public sealed partial class XenoSlimeCorpseSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private XenoSlimeHungerSystem _hunger = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private BodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Vanilla feeding (click a slime while holding food) ends up raising
        // IngestingEvent on whoever is eating. The slime's core organ is a stomach,
        // so vanilla feeding already works. We just add hunger on top.
        SubscribeLocalEvent<XenoSlimeComponent, IngestingEvent>(OnIngesting);
    }

    private void OnIngesting(Entity<XenoSlimeComponent> ent, ref IngestingEvent args)
    {
        _hunger.AddHunger(ent, ent.Comp.FeedHunger);
    }

    /// <summary>
    /// Can a slime eat this right now?
    /// </summary>
    public bool IsEdibleCorpse(EntityUid uid)
    {
        // TerminatingOrDeleted = "is this being deleted or already gone?"
        if (TerminatingOrDeleted(uid))
            return false;

        if (!TryComp<MobStateComponent>(uid, out var mobState) || !_mobState.IsDead(uid, mobState))
            return false;

        // Spec 15.1 is not decided yet: for now slimes do NOT eat dead slimes
        if (HasComp<XenoSlimeComponent>(uid))
            return false;

        // Inside a body bag, locker, someone's hands...
        if (_container.IsEntityInContainer(uid))
            return false;

        return true;
    }

    /// <summary>
    /// One bite. Returns true if the slime should keep eating.
    /// </summary>
    public bool Bite(Entity<XenoSlimeComponent> slime, EntityUid corpse)
    {
        // The bite counter lives on the CORPSE, so several slimes share it
        var bites = EnsureComp<XenoSlimeCorpseBitesComponent>(corpse);
        bites.Bites++;

        _hunger.AddHunger(slime, slime.Comp.CorpseBiteHunger);

        if (bites.Bites >= slime.Comp.CorpseBitesToFinish)
        {
            FinishCorpse(corpse);
            return false;
        }

        // Reached 150? Stop eating, it's time to split (spec 10.1)
        return slime.Comp.Mood != SlimeMood.Full;
    }

    private void FinishCorpse(EntityUid corpse)
    {
        // Drop the brain on the floor before deleting the body (spec 9.2.3).
        // This BodySystem helper is marked [Obsolete] in your version (still works,
        // just gives a warning). #pragma hides that one warning for these lines.
#pragma warning disable CS0618
        if (_body.TryGetOrgansWithComponent<BrainComponent>(corpse, out var brains))
#pragma warning restore CS0618
        {
            foreach (var brain in brains)
            {
                _container.TryRemoveFromContainer(brain.Owner);
                _transform.DropNextTo(brain.Owner, corpse);
            }
        }

        QueueDel(corpse);
    }
}
