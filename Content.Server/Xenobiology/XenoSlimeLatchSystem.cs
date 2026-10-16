using System.Numerics;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Xenobiology;

/// <summary>
/// Step 5: the 2-second attack, sticking to a victim, eating it alive, and letting go (spec 8).
/// The BRAIN decides who to attack. This system owns the RULES, so they also work
/// for player-controlled slimes later.
/// </summary>
public sealed partial class XenoSlimeLatchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private XenoSlimeHungerSystem _hunger = default!;

    // Slimes to unlatch after the Update loop (changing components mid-loop can crash,
    // same lesson as splitting).
    private readonly List<EntityUid> _toUnlatch = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoSlimeComponent, XenoSlimeLatchDoAfterEvent>(OnAttackFinished);
        SubscribeLocalEvent<XenoSlimeLatchedComponent, XenoSlimeResistDoAfterEvent>(OnResistFinished);
        SubscribeLocalEvent<XenoSlimeLatchedComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);

        // Clean up if either side is deleted
        SubscribeLocalEvent<XenoSlimeLatchedComponent, ComponentShutdown>(OnLatchedShutdown);
        SubscribeLocalEvent<XenoSlimeVictimComponent, ComponentShutdown>(OnVictimShutdown);
    }

    // ================= Starting the attack (spec 8.2) =================

    /// <summary>
    /// Can this slime start attacking this victim right now? (Range is checked by the do-after.)
    /// </summary>
    public bool CanLatch(EntityUid slime, EntityUid victim)
    {
        if (HasComp<XenoSlimeLatchedComponent>(slime))
            return false;

        // Only one slime per victim (spec 8.4)
        if (HasComp<XenoSlimeVictimComponent>(victim))
            return false;

        return _mobState.IsAlive(slime) && !_mobState.IsDead(victim);
    }

    /// <summary>
    /// Starts the 2-second attack progress bar. Returns the do-after ID so the brain can cancel it.
    /// </summary>
    public bool TryStartAttack(EntityUid slime, EntityUid victim, out DoAfterId? id)
    {
        id = null;

        if (!CanLatch(slime, victim))
            return false;

        var feeder = EnsureComp<XenoSlimeFeederComponent>(slime);

        var args = new DoAfterArgs(EntityManager, slime, feeder.AttackTime, new XenoSlimeLatchDoAfterEvent(),
            eventTarget: slime, target: victim)
        {
            DistanceThreshold = feeder.AttackRange + 0.5f,  // cancel if the victim gets away
            BreakOnMove = false,                            // the victim moving is handled by distance
            BlockDuplicate = true,
        };

        return _doAfter.TryStartDoAfter(args, out id);
    }

    private void OnAttackFinished(Entity<XenoSlimeComponent> ent, ref XenoSlimeLatchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } victim)
            return;

        args.Handled = true;

        // Several slimes may attack at once: the first to finish wins (spec 8.4)
        if (!CanLatch(ent, victim))
            return;

        Latch(ent, victim);
    }

    // ================= Sticking on (spec 8.3) =================

    private void Latch(EntityUid slime, EntityUid victim)
    {
        var feeder = EnsureComp<XenoSlimeFeederComponent>(slime);

        var latched = EnsureComp<XenoSlimeLatchedComponent>(slime);
        latched.Victim = victim;
        latched.NextBite = _timing.CurTime + feeder.BiteInterval;

        var victimComp = EnsureComp<XenoSlimeVictimComponent>(victim);
        victimComp.Slime = slime;

        // Make the slime a "child" of the victim, so it moves with them.
        // new EntityCoordinates(victim, Vector2.Zero) = "exactly on top of the victim"
        _transform.SetCoordinates(slime, new EntityCoordinates(victim, Vector2.Zero));

        _popup.PopupEntity("The slime latches onto you!", victim, victim, PopupType.LargeCaution);
    }

    // ================= Eating every second (spec 8.5) + letting go (spec 8.6) =================

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        _toUnlatch.Clear();

        // Only latched slimes have this component, so this loop is tiny
        var query = EntityQueryEnumerator<XenoSlimeLatchedComponent, XenoSlimeComponent>();

        while (query.MoveNext(out var uid, out var latched, out var slime))
        {
            // These checks are cheap, so do them every tick for a quick reaction
            if (ShouldLetGo(uid, latched))
            {
                _toUnlatch.Add(uid);
                continue;
            }

            if (now < latched.NextBite)
                continue;

            var feeder = EnsureComp<XenoSlimeFeederComponent>(uid);
            latched.NextBite = now + feeder.BiteInterval;

            Bite((uid, slime), feeder, latched.Victim);

            // 150 hunger: let go and split (spec 8.6.6)
            if (slime.Mood == SlimeMood.Full || _mobState.IsDead(latched.Victim))
                _toUnlatch.Add(uid);
        }

        foreach (var uid in _toUnlatch)
            Unlatch(uid);
    }

    private bool ShouldLetGo(EntityUid slime, XenoSlimeLatchedComponent latched)
    {
        // Victim gone or dead (spec 8.6.1)
        if (TerminatingOrDeleted(latched.Victim) || _mobState.IsDead(latched.Victim))
            return true;

        // Slime died (spec 8.6.5)
        if (!_mobState.IsAlive(slime))
            return true;

        // Someone is dragging the slime (spec 8.6.3)
        if (TryComp<PullableComponent>(slime, out var pullable) && pullable.BeingPulled)
            return true;

        // Sucked into a Xenovac (spec 8.6.4), or any other container
        if (_container.IsEntityInContainer(slime))
            return true;

        return false;
    }

    private void Bite(Entity<XenoSlimeComponent> slime, XenoSlimeFeederComponent feeder, EntityUid victim)
    {
        // Rabid deals double (spec 8.5.3). "*" on a DamageSpecifier multiplies every type.
        var damage = slime.Comp.Mood == SlimeMood.Rabid
            ? feeder.BiteDamage * feeder.RabidDamageMultiplier
            : feeder.BiteDamage;

        _damageable.TryChangeDamage(victim, damage, origin: slime);
        _hunger.AddHunger(slime, feeder.BiteHunger);
    }

    /// <summary>
    /// Lets go of the victim and drops the slime on the floor. Safe to call any time.
    /// </summary>
    public void Unlatch(EntityUid slime)
    {
        if (!TryComp<XenoSlimeLatchedComponent>(slime, out var latched))
            return;

        var victim = latched.Victim;

        // RemComp triggers OnLatchedShutdown, which does the actual cleanup
        RemComp<XenoSlimeLatchedComponent>(slime);

        if (Exists(victim) && TryComp<XenoSlimeVictimComponent>(victim, out var v) && v.Slime == slime)
            RemComp<XenoSlimeVictimComponent>(victim);
    }

    private void OnLatchedShutdown(Entity<XenoSlimeLatchedComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        // Still sitting on the victim? Put it back on the floor.
        // (If a Xenovac sucked it up, it's in a container: leave it there.)
        if (!_container.IsEntityInContainer(ent) && Transform(ent).ParentUid == ent.Comp.Victim)
            _transform.AttachToGridOrMap(ent);
    }

    private void OnVictimShutdown(Entity<XenoSlimeVictimComponent> ent, ref ComponentShutdown args)
    {
        // The victim is being deleted (gibbed, etc.): the slime must let go too
        if (TryComp<XenoSlimeLatchedComponent>(ent.Comp.Slime, out var latched) && latched.Victim == ent.Owner)
            RemCompDeferred<XenoSlimeLatchedComponent>(ent.Comp.Slime);
    }

    // ================= Victim resisting (spec 8.6.2) =================

    // Right-click the slime → "Pull the slime off". Only the victim gets this option.
    private void OnGetVerbs(Entity<XenoSlimeLatchedComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (args.User != ent.Comp.Victim)
            return;

        var user = args.User;
        var slime = ent.Owner;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Pull the slime off",
            // () => ... = a tiny function (a lambda) that runs when the verb is clicked
            Act = () => StartResist(user, slime),
        });
    }

    private void StartResist(EntityUid victim, EntityUid slime)
    {
        var feeder = EnsureComp<XenoSlimeFeederComponent>(slime);

        var args = new DoAfterArgs(EntityManager, victim, feeder.ResistTime, new XenoSlimeResistDoAfterEvent(),
            eventTarget: slime, target: slime)
        {
            BreakOnMove = false,
            BreakOnDamage = false,      // the slime damages them every second!
            BlockDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity("You struggle to pull the slime off...", victim, victim);
    }

    private void OnResistFinished(Entity<XenoSlimeLatchedComponent> ent, ref XenoSlimeResistDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        Unlatch(ent);
        _popup.PopupEntity("You pull the slime off!", args.User, args.User, PopupType.Medium);
    }
}
