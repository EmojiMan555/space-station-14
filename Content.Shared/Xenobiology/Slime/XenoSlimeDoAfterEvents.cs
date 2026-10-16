using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Xenobiology.Slime;

// Do-after events must live in Shared and be NetSerializable,
// because the progress bar is shown on the client.
// SimpleDoAfterEvent = "a do-after event that carries no extra data".

/// <summary>Slime finished its 2-second attack: try to latch on (spec 8.2-8.3).</summary>
[Serializable, NetSerializable]
public sealed partial class XenoSlimeLatchDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Victim finished its 5-second struggle: pull the slime off (spec 8.6.2).</summary>
[Serializable, NetSerializable]
public sealed partial class XenoSlimeResistDoAfterEvent : SimpleDoAfterEvent;
