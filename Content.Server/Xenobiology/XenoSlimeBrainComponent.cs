using System.Numerics;
using Content.Shared.DoAfter;

namespace Content.Server.Xenobiology;

// Server-only: the client never needs to know what a slime is thinking.
[RegisterComponent]
public sealed partial class XenoSlimeBrainComponent : Component
{
    // ---- Thinking (spec 6.1) ----
    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(2);

    public TimeSpan NextThink;

    // ---- Ranges (spec 6.2 + 6.3) ----
    [DataField]
    public float DetectionRange = 4f;

    [DataField]
    public float ChaseRange = 12f;

    // ---- Wandering (spec 7.2) ----
    [DataField]
    public float HopMinDistance = 1f;

    [DataField]
    public float HopMaxDistance = 3f;

    [DataField]
    public float RestMinSeconds = 5f;

    [DataField]
    public float RestMaxSeconds = 10f;

    // ---- Corpse eating (spec 9.2) ----
    [DataField]
    public float BiteRange = 1f;

    [DataField]
    public TimeSpan BiteInterval = TimeSpan.FromSeconds(1);

    // Targets we couldn't reach (no path, timed out) are ignored for this long (spec 6.6)
    [DataField]
    public TimeSpan IgnoreFailedTargetTime = TimeSpan.FromSeconds(10);

    // Give up walking to something if it takes longer than this
    [DataField]
    public TimeSpan MaxChaseTime = TimeSpan.FromSeconds(20);

    // ---- Memory (written by the system) ----
    public XenoSlimeBrainState State = XenoSlimeBrainState.Resting;

    public Vector2 HopDirection;
    public TimeSpan HopEnd;
    public TimeSpan NextHop;

    // ? = "nullable": can hold an entity OR null (nothing)
    public EntityUid? Target;
    public TimeSpan WalkDeadline;
    public TimeSpan NextBite;

    // The attack progress bar, so we can cancel it (pulled, target lost...)
    public DoAfterId? AttackDoAfter;

    // Dictionary = a lookup table: target → "ignore until this time"
    public Dictionary<EntityUid, TimeSpan> Ignored = new();
}

public enum XenoSlimeBrainState : byte
{
    Resting,
    Hopping,
    Busy,               // pulled or in a container (13.1)
    Splitting,          // Full / shaking (13.2)
    Latched,            // stuck on a victim, the latch system eats (13.3)
    Attacking,          // 2-second attack progress bar (13.4)
    Chasing,            // walking to a living target (13.5)
    GoingToCorpse,      // walking to a dead body (13.6)
    EatingCorpse,       // biting it every second (13.6)
}
