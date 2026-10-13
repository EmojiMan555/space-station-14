using Robust.Shared.Serialization;

namespace Content.Shared.Xenobiology;


[Serializable, NetSerializable]
public enum SlimeMood : byte
{
    Rabid, //hunger < 40
    Angry, //40 to 79
    Calm, //80 to 149
    Full, //hunger >= 150
}
