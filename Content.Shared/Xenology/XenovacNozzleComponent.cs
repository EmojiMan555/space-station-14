using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Xenology;

/// <summary>
/// Nozzle that sucks valid targets into a connected <see cref="XenovacTankComponent"/>.
/// Shooting the stored targets back out is handled by the gun system through the tank's ammo provider.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenovacSystem))]
public sealed partial class XenovacNozzleComponent : Component
{
    /// <summary>
    /// Which entities can be sucked up.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Which entities can never be sucked up, even if they pass the whitelist.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// How far away a target can be sucked up from.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 3f;

    /// <summary>
    /// How long it takes to suck up a target.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan SuckDelay = TimeSpan.FromSeconds(0.75);

    [DataField]
    public SoundSpecifier? SuckSound = new SoundPathSpecifier("/Audio/Effects/Fluids/vacuum-cleaner-fast.ogg");

    /// <summary>
    /// Played when a stored entity is launched out of the nozzle.
    /// </summary>
    [DataField]
    public SoundSpecifier? ShootSound = new SoundPathSpecifier("/Audio/Effects/thunk.ogg");
}
