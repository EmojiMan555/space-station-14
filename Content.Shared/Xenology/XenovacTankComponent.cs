using Robust.Shared.GameStates;

namespace Content.Shared.Xenology;

/// <summary>
/// Backpack tank that stores whatever a <see cref="XenovacNozzleComponent"/> sucked up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenovacSystem))]
public sealed partial class XenovacTankComponent : Component
{
    /// <summary>
    /// Container the stored entities are kept in. Should match the tank's ContainerAmmoProvider container.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ContainerId = "xenovac_storage";

    /// <summary>
    /// Maximum amount of entities the tank can hold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Capacity = 4;
}
