using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Xenobiology;

/// <summary>
/// On a SLIME while it's stuck to a victim and eating (spec 8.5).
/// Only latched slimes have it, so XenoSlimeLatchSystem.Update loops over very few entities.
/// </summary>
[RegisterComponent]
public sealed partial class XenoSlimeLatchedComponent : Component
{
    public EntityUid Victim;

    public TimeSpan NextBite;
}

/// <summary>
/// On a VICTIM while a slime is stuck to it. Makes "only one slime per victim" (spec 8.4) a simple check.
/// </summary>
[RegisterComponent]
public sealed partial class XenoSlimeVictimComponent : Component
{
    public EntityUid Slime;
}

/// <summary>
/// Latch / eating-alive settings. Put on the slime in YAML (all have defaults).
/// </summary>
[RegisterComponent]
public sealed partial class XenoSlimeFeederComponent : Component
{
    [DataField]
    public TimeSpan AttackTime = TimeSpan.FromSeconds(2);       // spec 8.2

    [DataField]
    public float AttackRange = 1.5f;                            // spec 8.2

    [DataField]
    public TimeSpan ResistTime = TimeSpan.FromSeconds(5);       // spec 8.6.2

    [DataField]
    public TimeSpan BiteInterval = TimeSpan.FromSeconds(1);     // spec 8.5

    [DataField]
    public double BiteHunger = 5;                               // spec 8.5.1

    // spec 8.5.2. Written in YAML like vanilla damage: types: { Caustic: 4, Heat: 2 }
    [DataField]
    public DamageSpecifier BiteDamage = new()
    {
        DamageDict = new()
        {
            { "Caustic", 4 },
            { "Heat", 2 },
        },
    };

    [DataField]
    public float RabidDamageMultiplier = 2f;                    // spec 8.5.3
}
