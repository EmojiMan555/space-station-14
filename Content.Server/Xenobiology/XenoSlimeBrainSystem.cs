using System.Numerics;
using Content.Server.NPC.Components;                // NPCSteeringComponent, SteeringStatus
using Content.Server.NPC.Systems;                   // NPCSteeringSystem (vanilla "legs" with pathfinding)
using Content.Shared.Damage.Components;             // DamageableComponent
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;               // MobStateComponent
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;           // InputMoverComponent, MovementSpeedModifierComponent
using Content.Shared.Movement.Events;               // SpriteMoveEvent
using Content.Shared.Movement.Pulling.Components;   // PullableComponent
using Content.Shared.NPC;                           // ActiveNPCComponent (the "awake" sticker)
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;                         // ActorComponent (= "a player controls this")
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Xenobiology;

public sealed partial class XenoSlimeBrainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private XenoSlimeCorpseSystem _corpse = default!;
    [Dependency] private XenoSlimeLatchSystem _latch = default!;

    // Re-used for every search, so searching creates no garbage (performance rule 4)
    private readonly HashSet<Entity<MobStateComponent>> _nearbyMobs = new();

    public override void Initialize()
    {
        base.Initialize();

        // Run BEFORE physics, so the movement code sees this tick's hop input.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));

        SubscribeLocalEvent<XenoSlimeBrainComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<XenoSlimeBrainComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        ent.Comp.NextThink = now + ent.Comp.ThinkInterval * _random.NextFloat();
        ent.Comp.NextHop = now + RandomRest(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var tick = _timing.CurTick;

        var query = EntityQueryEnumerator<XenoSlimeBrainComponent, XenoSlimeComponent, ActiveNPCComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var brain, out var slime, out _, out var mover))
        {
            // ---- Every tick: only the cheap "keep doing it" parts ----
            switch (brain.State)
            {
                case XenoSlimeBrainState.Hopping:
                    if (now < brain.HopEnd)
                        Push(mover, brain.HopDirection, tick);
                    else
                        StopAll((uid, brain));
                    break;

                case XenoSlimeBrainState.Chasing:
                    UpdateChasing((uid, brain), (uid, slime), now);
                    break;

                case XenoSlimeBrainState.Attacking:
                    UpdateAttacking((uid, brain));
                    break;

                case XenoSlimeBrainState.GoingToCorpse:
                    UpdateGoingToCorpse((uid, brain), now);
                    break;

                case XenoSlimeBrainState.EatingCorpse:
                    UpdateEatingCorpse((uid, brain), (uid, slime), now);
                    break;
            }

            // ---- Every 2 seconds: think ----
            if (now < brain.NextThink)
                continue;

            brain.NextThink = now + brain.ThinkInterval;
            Think((uid, brain), (uid, slime));
        }
    }

    // ===================================================================
    //  THE PRIORITY LIST (spec 13): top to bottom, the first one that applies wins
    // ===================================================================
    private void Think(Entity<XenoSlimeBrainComponent> ent, Entity<XenoSlimeComponent> slime)
    {
        var b = ent.Comp;

        // 13.1 Held, pulled or in a Xenovac: do nothing
        if (IsBusy(ent.Owner))
        {
            StopAll(ent);
            b.State = XenoSlimeBrainState.Busy;
            return;
        }

        // 13.3 Stuck on a victim: the latch system does the eating, we just wait
        if (HasComp<XenoSlimeLatchedComponent>(ent))
        {
            if (b.State != XenoSlimeBrainState.Latched)
            {
                StopAll(ent);
                b.State = XenoSlimeBrainState.Latched;
            }
            return;
        }

        // 13.2 Full or shaking: stand still, the split system does the rest
        if (slime.Comp.Mood == SlimeMood.Full || HasComp<XenoSlimeSplittingComponent>(ent))
        {
            StopAll(ent);
            b.State = XenoSlimeBrainState.Splitting;
            return;
        }

        // Coming back from one of the "waiting" states: rest first
        if (b.State is XenoSlimeBrainState.Busy or XenoSlimeBrainState.Splitting or XenoSlimeBrainState.Latched)
        {
            b.State = XenoSlimeBrainState.Resting;
            b.NextHop = _timing.CurTime + RandomRest(b);
        }

        // 13.4 Attack in progress: let the progress bar finish
        if (b.State == XenoSlimeBrainState.Attacking)
            return;

        // 13.5 Already chasing: keep going if the target is still valid
        // (its mood may have changed, e.g. Angry → Calm stops chasing players)
        if (b.State == XenoSlimeBrainState.Chasing)
        {
            if (b.Target is { } current && IsValidLivingTarget(slime, current))
                return;

            StopAll(ent);
        }

        // One search for both living targets and corpses (cheaper than two)
        FindTargets(ent, slime, out var living, out var corpse);

        // 13.4 / 13.5 New living target within 4 tiles. Beats eating a corpse.
        if (living is { } victim)
        {
            StopAll(ent);
            StartChase(ent, victim);
            return;
        }

        // 13.6 Corpse: already on it? The per-tick part handles it.
        if (b.State is XenoSlimeBrainState.GoingToCorpse or XenoSlimeBrainState.EatingCorpse)
            return;

        if (corpse is { } body)
        {
            StopAll(ent);
            StartGoingToCorpse(ent, body);
            return;
        }

        // 13.7 Merge happens in XenoSlimeMergeSystem (by touching), not here.

        // 13.8 Nothing else to do: wander
        Wander(ent);
    }

    private bool IsBusy(EntityUid uid)
    {
        if (_container.IsEntityInContainer(uid))
            return true;

        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
            return true;

        return false;
    }

    // ===================================================================
    //  STEP 4: CHOOSING TARGETS (spec 3, 5.4, 6.2, 6.5)
    // ===================================================================

    /// <summary>
    /// Is this a living thing this slime is willing to attack right now?
    /// </summary>
    public bool IsValidLivingTarget(Entity<XenoSlimeComponent> slime, EntityUid target)
    {
        if (target == slime.Owner || TerminatingOrDeleted(target))
            return false;

        // Must be a living mob that can take damage (spec 3.5)
        if (!TryComp<MobStateComponent>(target, out var mobState) || _mobState.IsDead(target, mobState))
            return false;

        if (!HasComp<DamageableComponent>(target))
            return false;

        // Never attack or eat living slimes (spec 3.5)
        if (HasComp<XenoSlimeComponent>(target))
            return false;

        if (_container.IsEntityInContainer(target))
            return false;

        // Another slime is already stuck on it (spec 8.4)
        if (TryComp<XenoSlimeVictimComponent>(target, out var victim) && victim.Slime != slime.Owner)
            return false;

        var isPlayer = HasComp<ActorComponent>(target);
        var isFriend = slime.Comp.Friends.Contains(target);

        // The mood table (spec 3.1 - 3.4 + 5.4)
        return slime.Comp.Mood switch
        {
            SlimeMood.Calm  => !isPlayer && !isFriend,   // non-player mobs only
            SlimeMood.Angry => !isFriend,                // + players who aren't friends
            SlimeMood.Rabid => true,                     // everyone, friends included
            _               => false,                    // Full: nothing
        };
    }

    private void FindTargets(Entity<XenoSlimeBrainComponent> ent, Entity<XenoSlimeComponent> slime,
        out EntityUid? living, out EntityUid? corpse)
    {
        living = null;
        corpse = null;

        var now = _timing.CurTime;
        var myPos = _transform.GetWorldPosition(ent);
        var bestLiving = float.MaxValue;
        var bestCorpse = float.MaxValue;

        _nearbyMobs.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.DetectionRange, _nearbyMobs);

        foreach (var mob in _nearbyMobs)
        {
            if (IsIgnored(ent.Comp, mob, now))
                continue;

            var distance = (_transform.GetWorldPosition(mob) - myPos).Length();

            // Closest one wins (spec 6.5)
            if (distance < bestLiving && IsValidLivingTarget(slime, mob))
            {
                bestLiving = distance;
                living = mob.Owner;
            }
            else if (distance < bestCorpse && _corpse.IsEdibleCorpse(mob))
            {
                bestCorpse = distance;
                corpse = mob.Owner;
            }
        }
    }

    // ===================================================================
    //  STEP 4: CHASING (spec 6.3, 6.4, 6.6)
    // ===================================================================

    private void StartChase(Entity<XenoSlimeBrainComponent> ent, EntityUid target)
    {
        ent.Comp.Target = target;

        // Already close enough? Attack straight away.
        if (DistanceTo(ent, target) <= AttackRange(ent))
        {
            StartAttack(ent, target);
            return;
        }

        ent.Comp.State = XenoSlimeBrainState.Chasing;
        ent.Comp.WalkDeadline = _timing.CurTime + ent.Comp.MaxChaseTime;
        StartSteering(ent, target, AttackRange(ent) - 0.3f);
    }

    // Every tick while chasing. Cheap: two positions + one status check.
    private void UpdateChasing(Entity<XenoSlimeBrainComponent> ent, Entity<XenoSlimeComponent> slime, TimeSpan now)
    {
        var b = ent.Comp;

        if (b.Target is not { } target || !IsValidLivingTarget(slime, target))
        {
            StopAll(ent);
            return;
        }

        var distance = DistanceTo(ent, target);

        // Ran more than 12 tiles away: give up immediately (spec 6.3 + 6.4)
        // No path (spec 6.6), or taking forever: ignore it for a while
        if (distance > b.ChaseRange || now > b.WalkDeadline || SteeringFailed(ent))
        {
            Ignore(b, target, now);
            StopAll(ent);
            return;
        }

        // Within 1.5 tiles: start the 2-second attack (spec 8.2)
        if (distance <= AttackRange(ent))
            StartAttack(ent, target);
    }

    // ===================================================================
    //  STEP 5: ATTACKING (spec 8.2 - 8.4)
    // ===================================================================

    private void StartAttack(Entity<XenoSlimeBrainComponent> ent, EntityUid target)
    {
        StopMovement(ent);

        if (!_latch.TryStartAttack(ent, target, out var id))
        {
            // Someone else got there first, etc. Pick something else next think.
            Ignore(ent.Comp, target, _timing.CurTime);
            StopAll(ent);
            return;
        }

        ent.Comp.State = XenoSlimeBrainState.Attacking;
        ent.Comp.Target = target;
        ent.Comp.AttackDoAfter = id;
    }

    private void UpdateAttacking(Entity<XenoSlimeBrainComponent> ent)
    {
        // The attack worked: we're stuck on the victim now
        if (HasComp<XenoSlimeLatchedComponent>(ent))
        {
            ent.Comp.AttackDoAfter = null;
            ent.Comp.Target = null;
            ent.Comp.State = XenoSlimeBrainState.Latched;
            return;
        }

        // Progress bar ended without latching (cancelled, victim escaped, someone else won)
        if (_doAfter.GetStatus(ent.Comp.AttackDoAfter) != DoAfterStatus.Running)
        {
            ent.Comp.AttackDoAfter = null;
            StopAll(ent);
        }
    }

    // ===================================================================
    //  STEP 7: CORPSES (spec 9.1 - 9.2), now with real pathfinding
    // ===================================================================

    private void StartGoingToCorpse(Entity<XenoSlimeBrainComponent> ent, EntityUid corpse)
    {
        var b = ent.Comp;
        b.Target = corpse;
        b.State = XenoSlimeBrainState.GoingToCorpse;
        b.WalkDeadline = _timing.CurTime + b.MaxChaseTime;
        StartSteering(ent, corpse, b.BiteRange - 0.2f);
    }

    private void UpdateGoingToCorpse(Entity<XenoSlimeBrainComponent> ent, TimeSpan now)
    {
        var b = ent.Comp;

        // "is not { } target" = "is null"; if it isn't, call it target
        if (b.Target is not { } target || !_corpse.IsEdibleCorpse(target))
        {
            StopAll(ent);
            return;
        }

        var distance = DistanceTo(ent, target);

        if (distance > b.ChaseRange || now > b.WalkDeadline || SteeringFailed(ent))
        {
            Ignore(b, target, now);
            StopAll(ent);
            return;
        }

        if (distance <= b.BiteRange)
        {
            StopMovement(ent);
            b.State = XenoSlimeBrainState.EatingCorpse;
            b.Target = target;
            b.NextBite = now;
        }
    }

    private void UpdateEatingCorpse(Entity<XenoSlimeBrainComponent> ent, Entity<XenoSlimeComponent> slime, TimeSpan now)
    {
        var b = ent.Comp;

        if (now < b.NextBite)
            return;

        b.NextBite = now + b.BiteInterval;

        if (b.Target is not { } target || !_corpse.IsEdibleCorpse(target)
            || DistanceTo(ent, target) > b.BiteRange + 0.5f)
        {
            StopAll(ent);
            return;
        }

        if (!_corpse.Bite(slime, target))
            StopAll(ent);
    }

    // ===================================================================
    //  WANDERING (spec 7): direct push, no pathfinding, no steering
    // ===================================================================

    private void Wander(Entity<XenoSlimeBrainComponent> ent)
    {
        var b = ent.Comp;
        var now = _timing.CurTime;

        if (b.State == XenoSlimeBrainState.Hopping || now < b.NextHop)
            return;

        var speed = GetSpeed(ent);
        if (speed <= 0f)
            return;

        var distance = _random.NextFloat(b.HopMinDistance, b.HopMaxDistance);

        b.HopDirection = _random.NextAngle().ToVec();
        b.HopEnd = now + TimeSpan.FromSeconds(distance / speed);
        b.State = XenoSlimeBrainState.Hopping;
        SetMovingSprite(ent, true);
    }

    // ===================================================================
    //  HELPERS
    // ===================================================================

    // Vanilla steering walks to a moving entity and paths around walls.
    // Only CHASING slimes use it, so the expensive part only runs for a few slimes.
    private void StartSteering(EntityUid uid, EntityUid target, float stopRange)
    {
        // Coordinates relative to the target itself = "follow it wherever it goes"
        var steering = _steering.Register(uid, new EntityCoordinates(target, Vector2.Zero));
        steering.Range = Math.Max(stopRange, 0.2f);
    }

    private bool SteeringFailed(EntityUid uid)
    {
        return TryComp<NPCSteeringComponent>(uid, out var steering)
               && steering.Status == SteeringStatus.NoPath;
    }

    /// <summary>
    /// Stops moving, cancels any attack, forgets the target, and goes back to resting.
    /// public: other systems may need to calm a slime down.
    /// </summary>
    public void StopAll(Entity<XenoSlimeBrainComponent> ent)
    {
        var b = ent.Comp;

        StopMovement(ent);

        if (b.AttackDoAfter != null)
        {
            _doAfter.Cancel(b.AttackDoAfter);
            b.AttackDoAfter = null;
        }

        if (b.State is XenoSlimeBrainState.Hopping
            or XenoSlimeBrainState.Chasing
            or XenoSlimeBrainState.Attacking
            or XenoSlimeBrainState.GoingToCorpse
            or XenoSlimeBrainState.EatingCorpse)
        {
            b.State = XenoSlimeBrainState.Resting;
            b.NextHop = _timing.CurTime + RandomRest(b);
        }

        b.Target = null;
    }

    // Just the movement part: stop hopping and stop steering
    private void StopMovement(Entity<XenoSlimeBrainComponent> ent)
    {
        if (ent.Comp.State == XenoSlimeBrainState.Hopping)
            SetMovingSprite(ent, false);

        if (HasComp<NPCSteeringComponent>(ent))
            _steering.Unregister(ent);
    }

    private void Ignore(XenoSlimeBrainComponent b, EntityUid target, TimeSpan now)
    {
        b.Ignored[target] = now + b.IgnoreFailedTargetTime;
    }

    private bool IsIgnored(XenoSlimeBrainComponent b, EntityUid target, TimeSpan now)
    {
        if (!b.Ignored.TryGetValue(target, out var until))
            return false;

        if (now < until)
            return true;

        b.Ignored.Remove(target);   // expired: forget it
        return false;
    }

    private float DistanceTo(EntityUid uid, EntityUid target)
    {
        return (_transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid)).Length();
    }

    private float AttackRange(EntityUid uid)
    {
        // CompOrNull = the component, or null if it doesn't have one
        // ?? = "if the left side is null, use the right side"
        return CompOrNull<XenoSlimeFeederComponent>(uid)?.AttackRange ?? 1.5f;
    }

    private static void Push(InputMoverComponent mover, Vector2 direction, GameTick tick)
    {
        mover.CurTickSprintMovement = direction;
        mover.LastInputTick = tick;
        mover.LastInputSubTick = ushort.MaxValue;
    }

    private void SetMovingSprite(EntityUid uid, bool moving)
    {
        var ev = new SpriteMoveEvent(moving);
        RaiseLocalEvent(uid, ref ev);
    }

    private float GetSpeed(EntityUid uid)
    {
        return TryComp<MovementSpeedModifierComponent>(uid, out var move)
            ? move.CurrentSprintSpeed
            : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
    }

    private TimeSpan RandomRest(XenoSlimeBrainComponent b)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(b.RestMinSeconds, b.RestMaxSeconds));
    }
}
