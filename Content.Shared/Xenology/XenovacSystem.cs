using Content.Shared.Containers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Xenology;

/// <summary>
/// Handles the Xenovac: a Slime Rancher style vacpack that sucks creatures into a backpack tank.
/// Shooting them back out is done by the gun system, through the tank's ContainerAmmoProvider
/// and the nozzle's ClothingSlotAmmoProvider.
/// </summary>
public sealed partial class XenovacSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SlotBasedConnectedContainerSystem _connectedContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenovacNozzleComponent, AfterInteractEvent>(OnNozzleAfterInteract);
        SubscribeLocalEvent<XenovacNozzleComponent, XenovacSuckDoAfterEvent>(OnSuckDoAfter);
        SubscribeLocalEvent<XenovacNozzleComponent, AttemptShootEvent>(OnNozzleAttemptShoot);
        SubscribeLocalEvent<XenovacNozzleComponent, GunShotEvent>(OnNozzleShot);

        SubscribeLocalEvent<XenovacTankComponent, ComponentInit>(OnTankInit);
        SubscribeLocalEvent<XenovacTankComponent, ExaminedEvent>(OnTankExamined);
        SubscribeLocalEvent<XenovacTankComponent, GetVerbsEvent<AlternativeVerb>>(OnTankGetVerbs);
        SubscribeLocalEvent<XenovacTankComponent, EntityTerminatingEvent>(OnTankTerminating);
    }

    private void OnNozzleAfterInteract(Entity<XenovacNozzleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        if (!CanSuck(ent, args.User, target))
            return;

        args.Handled = true;

        if (!_interaction.InRangeUnobstructed(args.User, target, ent.Comp.Range))
        {
            _popup.PopupClient(Loc.GetString("xenovac-popup-out-of-range"), ent, args.User);
            return;
        }

        if (!TryGetTank(ent, out var tank, out var container))
        {
            _popup.PopupClient(Loc.GetString("xenovac-popup-no-tank"), ent, args.User);
            return;
        }

        if (container.ContainedEntities.Count >= tank.Value.Comp.Capacity)
        {
            _popup.PopupClient(Loc.GetString("xenovac-popup-tank-full"), ent, args.User);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.SuckDelay, new XenovacSuckDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = ent.Comp.Range,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            _audio.PlayPredicted(ent.Comp.SuckSound, ent, args.User);
    }

    private void OnSuckDoAfter(Entity<XenovacNozzleComponent> ent, ref XenovacSuckDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CanSuck(ent, args.User, target) || !TryGetTank(ent, out var tank, out var container))
            return;

        if (container.ContainedEntities.Count >= tank.Value.Comp.Capacity)
        {
            _popup.PopupClient(Loc.GetString("xenovac-popup-tank-full"), ent, args.User);
            return;
        }

        if (!_container.Insert(target, container))
            return;

        args.Handled = true;
        _popup.PopupClient(Loc.GetString("xenovac-popup-sucked",
                ("target", target),
                ("count", container.ContainedEntities.Count),
                ("capacity", tank.Value.Comp.Capacity)),
            ent,
            args.User);
    }

    private void OnNozzleAttemptShoot(Entity<XenovacNozzleComponent> ent, ref AttemptShootEvent args)
    {
        // Stored creatures are launched out of the nozzle, not fired as bullets.
        args.ThrowItems = true;
    }

    private void OnNozzleShot(Entity<XenovacNozzleComponent> ent, ref GunShotEvent args)
    {
        // Thrown ammo doesn't play the gun's gunshot sound, so play our own.
        if (args.Ammo.Count > 0)
            _audio.PlayPredicted(ent.Comp.ShootSound, ent, args.User);
    }

    private void OnTankInit(Entity<XenovacTankComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
    }

    private void OnTankExamined(Entity<XenovacTankComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        args.PushMarkup(Loc.GetString("xenovac-tank-examine",
            ("count", container.ContainedEntities.Count),
            ("capacity", ent.Comp.Capacity)));
    }

    private void OnTankGetVerbs(Entity<XenovacTankComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        if (!_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container)
            || container.ContainedEntities.Count == 0)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("xenovac-verb-empty"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
            Act = () => EmptyTank(ent, user),
        });
    }

    private void OnTankTerminating(Entity<XenovacTankComponent> ent, ref EntityTerminatingEvent args)
    {
        // Don't delete the poor creatures along with the tank.
        if (!_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        var coords = _transform.GetMoverCoordinates(ent);
        if (TerminatingOrDeleted(coords.EntityId))
            return;

        _container.EmptyContainer(container, destination: coords);
    }

    /// <summary>
    /// Drops everything stored in the tank on the floor.
    /// </summary>
    public void EmptyTank(Entity<XenovacTankComponent> ent, EntityUid? user = null)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        _container.EmptyContainer(container, destination: _transform.GetMoverCoordinates(ent));

        if (user != null)
            _popup.PopupClient(Loc.GetString("xenovac-popup-emptied"), ent, user.Value);
    }

    private bool CanSuck(Entity<XenovacNozzleComponent> ent, EntityUid user, EntityUid target)
    {
        if (target == user || target == ent.Owner)
            return false;

        if (Transform(target).Anchored || _container.IsEntityInContainer(target))
            return false;

        return _whitelist.CheckBoth(target, ent.Comp.Blacklist, ent.Comp.Whitelist);
    }

    private bool TryGetTank(EntityUid nozzle,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entity<XenovacTankComponent>? tank,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BaseContainer? container)
    {
        tank = null;
        container = null;

        if (!_connectedContainer.TryGetConnectedContainer(nozzle, out var tankUid)
            || !TryComp<XenovacTankComponent>(tankUid, out var tankComp)
            || !_container.TryGetContainer(tankUid.Value, tankComp.ContainerId, out container))
            return false;

        tank = (tankUid.Value, tankComp);
        return true;
    }
}

[Serializable, NetSerializable]
public sealed partial class XenovacSuckDoAfterEvent : SimpleDoAfterEvent;
