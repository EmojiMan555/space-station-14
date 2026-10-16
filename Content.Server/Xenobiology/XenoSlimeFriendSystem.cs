using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.Random;

namespace Content.Server.Xenobiology;

/// <summary>
/// Step 9: petting a slime may make you its friend (spec 5).
/// Vanilla InteractionPopup handles the petting click and raises
/// InteractionSuccessEvent. We roll the friendship chance on top of it.
/// </summary>
public sealed partial class XenoSlimeFriendSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoSlimeComponent, InteractionSuccessEvent>(OnPetted);
    }

    private void OnPetted(Entity<XenoSlimeComponent> ent, ref InteractionSuccessEvent args)
    {
        var user = args.User;

        if (ent.Comp.Friends.Contains(user))
            return;

        // Chance per pet by mood (spec 5.2). Full counts as Calm (it's about to split anyway).
        var chance = ent.Comp.Mood switch
        {
            SlimeMood.Calm or SlimeMood.Full => ent.Comp.FriendChanceCalm,
            SlimeMood.Angry                  => ent.Comp.FriendChanceAngry,
            _                                => 0f,   // Rabid: never
        };

        if (!_random.Prob(chance))
        {
            _popup.PopupEntity("The slime doesn't seem to trust you yet.", ent, user);
            return;
        }

        ent.Comp.Friends.Add(user);
        _popup.PopupEntity("The slime wobbles happily. You're friends now!", ent, user, PopupType.Medium);
    }

    public bool IsFriend(Entity<XenoSlimeComponent> slime, EntityUid other)
    {
        return slime.Comp.Friends.Contains(other);
    }
}
