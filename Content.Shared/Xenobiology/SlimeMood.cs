using Robust.Shared.Serialization;

namespace Content.Shared.Xenobiology;


[Serializable, NetSerializable]
public enum SlimeMood : byte
{
    Rabid, //hunger < 40
    Angry, //41 to 80
    Calm, //81 to 149
    Full, //hunger >= 150
}
