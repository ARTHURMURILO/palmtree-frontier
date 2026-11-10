using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server._PS.Circuitry.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Hands.Systems;
using Content.Server.NPC.Systems;
using Content.Server.PowerCell;
using Content.Server.Radio.Components;
using Content.Shared._PS.Circuitry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: owns the physical assembly. Slot contents are authoritative; every capability cache
/// is derived from the installed modules, never from the Lua program.
/// </summary>
public sealed class CircuitAssemblySystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SpeechSystem _speech = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CircuitAssemblyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CircuitAssemblyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CircuitAssemblyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CircuitAssemblyComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<CircuitAssemblyComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<CircuitAssemblyComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CircuitAssemblyComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<CircuitAssemblyComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CircuitAssemblyComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, CircuitAssemblyComponent component, MapInitEvent args)
    {
        component.NextIdlePowerDraw = _timing.CurTime;
        Recompute(uid, component);
    }

    /// <summary>
    /// Loaded entities keep <c>mapInit: true</c>, so the map loader does not raise <see cref="MapInitEvent"/>
    /// again. Rebuild the derived slot caches when the component starts so a Bluespace Dock reload has the
    /// CPU, cell, modules and capabilities it was saved with.
    /// </summary>
    private void OnStartup(EntityUid uid, CircuitAssemblyComponent component, ComponentStartup args)
    {
        Recompute(uid, component);
    }

    private void OnShutdown(EntityUid uid, CircuitAssemblyComponent component, ComponentShutdown args)
    {
        _steering.Unregister(uid);
    }

    private void OnSlotChanged(EntityUid uid, CircuitAssemblyComponent component, EntInsertedIntoContainerMessage args)
    {
        if (component.Slots.ContainsKey(args.Container.ID))
            Recompute(uid, component);
    }

    private void OnSlotChanged(EntityUid uid, CircuitAssemblyComponent component, EntRemovedFromContainerMessage args)
    {
        if (component.Slots.ContainsKey(args.Container.ID))
            Recompute(uid, component);
    }

    private void OnMobStateChanged(EntityUid uid, CircuitAssemblyComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            StopMovement(uid, component);

        RefreshPowerState(uid, component);
        RaiseLocalEvent(uid, new CircuitAssemblyChangedEvent());
    }

    private void OnExamined(EntityUid uid, CircuitAssemblyComponent component, ExaminedEvent args)
    {
        var modules = component.Manipulators.Count + component.Analyzers.Count +
                      component.Beakers.Count(beaker => beaker != null) +
                      (component.Locomotion != null ? 1 : 0) +
                      (component.Voice != null ? 1 : 0) + (component.Sensor != null ? 1 : 0) +
                      (component.Radio != null ? 1 : 0);

        args.PushMarkup(Loc.GetString("circuit-assembly-examine",
            ("modules", modules),
            ("power", component.Powered ? Loc.GetString("circuit-state-powered") : Loc.GetString("circuit-state-unpowered"))));
    }

    private void OnInteractUsing(EntityUid uid, CircuitAssemblyComponent component, InteractUsingEvent args)
    {
        if (args.Handled || args.Used == uid)
            return;

        if (IsCircuitPart(args.Used) && !CanModify(uid, args.User, component))
        {
            _popup.PopupEntity(Loc.GetString("circuit-assembly-locked", ("owner", component.OwnerName ?? "unknown")), uid, args.User);
            args.Handled = true;
            return;
        }

        // Encryption keys are installed into the radio module, not the chassis itself.
        if (component.Radio is { } radio &&
            HasComp<EncryptionKeyComponent>(args.Used))
        {
            if (!CanModify(uid, args.User, component))
            {
                _popup.PopupEntity(Loc.GetString("circuit-assembly-locked", ("owner", component.OwnerName ?? "unknown")), uid, args.User);
                args.Handled = true;
                return;
            }

            RaiseLocalEvent(radio, args);
            if (args.Handled)
                return;
        }

        if (!TryFindSlotForItem(uid, args.Used, component, out var slotId))
        {
            if (IsCircuitPart(args.Used))
            {
                _popup.PopupEntity(Loc.GetString("circuit-assembly-no-free-slot"), uid, args.User);
                args.Handled = true;
            }

            return;
        }

        if (_itemSlots.TryInsert(uid, slotId, args.Used, args.User))
            args.Handled = true;
    }

    private void OnInteractHand(EntityUid uid, CircuitAssemblyComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (_ui.TryOpenUi(uid, CircuitAssemblyUiKey.Key, args.User))
            args.Handled = true;
    }

    public void Recompute(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || TerminatingOrDeleted(uid))
            return;

        component.Capabilities = CircuitCapability.None;
        component.Cpu = null;
        component.Cell = null;
        component.Locomotion = null;
        component.Voice = null;
        component.Sensor = null;
        component.Radio = null;
        component.Beakers = new List<EntityUid?> { null, null, null, null };
        component.Analyzers.Clear();
        component.Manipulators.Clear();
        component.Hands = 0;
        component.SensorRange = 0f;
        component.MaxScanResults = 0;
        component.HandRange = 1.5f;
        component.IdlePowerDraw = 0.05f;
        component.MovePowerPerMeter = 0f;
        component.ActionPowerCost = 0.05f;
        component.CanFly = false;
        component.LastMovePosition = Transform(uid).LocalPosition;

        var walkSpeed = 0f;
        var sprintSpeed = 0f;
        var acceleration = 0f;
        var canFly = false;
        var desiredHands = new HashSet<string>();
        var allManipulatorHands = new HashSet<string>();

        foreach (var (slotId, role) in component.Slots)
        {
            if (role == CircuitSlotRole.Manipulator)
                allManipulatorHands.Add(GetHandId(slotId));

            if (!_itemSlots.TryGetSlot(uid, slotId, out var slot) || slot.Item is not { } item)
                continue;

            switch (role)
            {
                case CircuitSlotRole.Cpu:
                    if (!TryComp<CircuitCpuComponent>(item, out var cpu))
                        break;

                    component.Cpu = item;
                    component.Capabilities |= CircuitCapability.Cpu;
                    component.IdlePowerDraw += cpu.IdlePowerDraw;
                    break;

                case CircuitSlotRole.Cell:
                    if (!HasComp<PowerCellComponent>(item))
                        break;

                    component.Cell = item;
                    component.Capabilities |= CircuitCapability.Power;
                    break;

                case CircuitSlotRole.Locomotion:
                    if (!TryComp<CircuitPartComponent>(item, out var locomotion) ||
                        locomotion.PartType != CircuitPartType.Locomotion)
                    {
                        break;
                    }

                    component.Locomotion = item;
                    component.Capabilities |= CircuitCapability.Locomotion;
                    component.IdlePowerDraw += locomotion.IdlePowerDraw;
                    component.MovePowerPerMeter = MathF.Max(component.MovePowerPerMeter, locomotion.MovePowerPerMeter);
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, locomotion.ActionPowerCost);
                    walkSpeed = MathF.Max(walkSpeed, locomotion.WalkSpeed);
                    sprintSpeed = MathF.Max(sprintSpeed, locomotion.SprintSpeed);
                    acceleration = MathF.Max(acceleration, locomotion.Acceleration);
                    canFly |= locomotion.CanMoveInAir || locomotion.IgnoreGravity;

                    if (locomotion.IgnoreGravity || locomotion.CanMoveInAir)
                        component.Capabilities |= CircuitCapability.Flight;
                    break;

                case CircuitSlotRole.Manipulator:
                    if (!TryComp<CircuitPartComponent>(item, out var manipulator) ||
                        manipulator.PartType != CircuitPartType.Manipulator)
                    {
                        break;
                    }

                    component.Manipulators.Add(item);
                    component.Capabilities |= CircuitCapability.Manipulator;
                    component.HandRange = MathF.Max(component.HandRange, manipulator.HandRange);
                    component.IdlePowerDraw += manipulator.IdlePowerDraw;
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, manipulator.ActionPowerCost);
                    desiredHands.Add(GetHandId(slotId));
                    break;

                case CircuitSlotRole.Voice:
                    if (!TryComp<CircuitPartComponent>(item, out var voice) || voice.PartType != CircuitPartType.Voice)
                        break;

                    component.Voice = item;
                    component.Capabilities |= CircuitCapability.Voice;
                    component.IdlePowerDraw += voice.IdlePowerDraw;
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, voice.ActionPowerCost);
                    break;

                case CircuitSlotRole.Sensor:
                    if (!TryComp<CircuitPartComponent>(item, out var sensor) || sensor.PartType != CircuitPartType.Sensor)
                        break;

                    component.Sensor = item;
                    component.Capabilities |= CircuitCapability.Sensor;
                    component.SensorRange = MathF.Max(component.SensorRange, sensor.SensorRange);
                    component.MaxScanResults = Math.Max(component.MaxScanResults, sensor.MaxScanResults);
                    component.IdlePowerDraw += sensor.IdlePowerDraw;
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, sensor.ActionPowerCost);
                    break;

                case CircuitSlotRole.Radio:
                    if (!TryComp<CircuitPartComponent>(item, out var radio) ||
                        radio.PartType != CircuitPartType.Radio)
                    {
                        break;
                    }

                    component.Radio = item;
                    component.Capabilities |= CircuitCapability.Radio;
                    component.IdlePowerDraw += radio.IdlePowerDraw;
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, radio.ActionPowerCost);
                    break;

                case CircuitSlotRole.Analyzer:
                    if (!TryComp<CircuitPartComponent>(item, out var analyzer) ||
                        analyzer.PartType != CircuitPartType.Analyzer)
                    {
                        break;
                    }

                    component.Analyzers.Add(item);
                    component.Capabilities |= analyzer.AnalyzerType switch
                    {
                        CircuitAnalyzerType.Botanical => CircuitCapability.BotanicalAnalyzer,
                        CircuitAnalyzerType.Chemical => CircuitCapability.ChemicalAnalyzer,
                        CircuitAnalyzerType.Medical => CircuitCapability.MedicalAnalyzer,
                        CircuitAnalyzerType.Gas => CircuitCapability.GasAnalyzer,
                        _ => CircuitCapability.None,
                    };

                    component.IdlePowerDraw += analyzer.IdlePowerDraw;
                    component.ActionPowerCost = MathF.Max(component.ActionPowerCost, analyzer.ActionPowerCost);
                    break;

                case CircuitSlotRole.Reagent:
                    if (!HasComp<SolutionContainerManagerComponent>(item) ||
                        !TryGetBeakerIndex(slotId, out var beakerIndex))
                    {
                        break;
                    }

                    component.Beakers[beakerIndex - 1] = item;
                    component.Capabilities |= CircuitCapability.Reagent;
                    break;
            }
        }

        // Hands are tied to manipulator slot IDs so removing an arm drops whatever it held.
        foreach (var handId in desiredHands)
        {
            if (_hands.TryGetHand(uid, handId, out _))
                continue;

            var location = handId.Contains("right_arm", StringComparison.Ordinal)
                ? HandLocation.Left
                : HandLocation.Right;
            _hands.AddHand(uid, handId, location, emptyLabel: "circuit-hand-label");
        }

        foreach (var handId in allManipulatorHands)
        {
            if (desiredHands.Contains(handId) || !_hands.TryGetHand(uid, handId, out _))
                continue;

            _hands.TryDrop(uid, handId, checkActionBlocker: false);
            _hands.RemoveHand(uid, handId);
        }

        component.Hands = desiredHands.Count;

        var hasLocomotion = component.Capabilities.HasFlag(CircuitCapability.Locomotion);
        var dead = _mobState.IsDead(uid);

        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            mover.CanMove = hasLocomotion && !dead;
            Dirty(uid, mover);
        }

        if (TryComp<MovementSpeedModifierComponent>(uid, out var speedModifier))
        {
            _movement.ChangeBaseSpeed(uid, walkSpeed, sprintSpeed, acceleration, speedModifier);
            _movement.RefreshMovementSpeedModifiers(uid, speedModifier);
        }

        if (hasLocomotion)
            EnsureComp<ActiveNPCComponent>(uid);
        else
        {
            RemComp<ActiveNPCComponent>(uid);
            _steering.Unregister(uid);
            StopMovement(uid, component);
        }

        if (canFly)
        {
            var gravity = EnsureComp<MovementIgnoreGravityComponent>(uid);
            gravity.Weightless = true;
            EnsureComp<CanMoveInAirComponent>(uid);
        }
        else
        {
            RemComp<MovementIgnoreGravityComponent>(uid);
            RemComp<CanMoveInAirComponent>(uid);
        }

        component.CanFly = canFly;
        _speech.SetSpeech(uid, component.Capabilities.HasFlag(CircuitCapability.Voice));

        RefreshPowerState(uid, component);
        RefreshRadio(uid, component);
        RaiseLocalEvent(uid, new CircuitAssemblyChangedEvent());
    }

    /// <summary>
    /// Keeps the assembly's passive radio listener in sync with the radio module's installed keys.
    /// The module's encryption keys are the sole source of allowed channels.
    /// </summary>
    public void RefreshRadio(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (component.Radio is not { } radio ||
            !component.Capabilities.HasFlag(CircuitCapability.Radio))
        {
            RemComp<ActiveRadioComponent>(uid);
            RemComp<DeviceLinkSinkComponent>(uid);
            RemComp<DeviceLinkSourceComponent>(uid);
            return;
        }

        // The radio module doubles as the assembly's wireless device-link transceiver. A remote
        // signaller can link its Pressed source port to this Trigger sink, and the assembly can
        // link its Pressed source to other devices.
        _deviceLink.EnsureSinkPorts(uid, "Trigger", "On", "Off");
        _deviceLink.EnsureSourcePorts(uid, "Pressed", "On", "Off");

        if (!TryComp<EncryptionKeyHolderComponent>(radio, out var keys) || keys.Channels.Count == 0)
        {
            RemComp<ActiveRadioComponent>(uid);
            return;
        }

        var active = EnsureComp<ActiveRadioComponent>(uid);
        active.Channels = new HashSet<string>(keys.Channels);
    }

    /// <summary>Rechecks the cell and gates self-movement. Called by the power system and on slot changes.</summary>
    public void RefreshPowerState(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.Powered = _powerCell.TryGetBatteryFromSlot(uid, out var battery) && battery.CurrentCharge > 0f;

        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            var canMove = component.Powered &&
                          component.Capabilities.HasFlag(CircuitCapability.Locomotion) &&
                          !_mobState.IsDead(uid);

            if (mover.CanMove != canMove)
            {
                mover.CanMove = canMove;
                Dirty(uid, mover);
            }
        }

        if (!component.Powered && component.WalkMode != CircuitWalkMode.Idle)
            StopMovement(uid, component);
    }

    public bool TryConsumePower(EntityUid uid, float amount, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        if (amount <= 0f)
            return true;

        if (_powerCell.TryUseCharge(uid, amount))
            return true;

        RefreshPowerState(uid, component);
        return false;
    }

    public bool TryGetPower(EntityUid uid, out float current, out float max)
    {
        current = 0f;
        max = 0f;

        if (!_powerCell.TryGetBatteryFromSlot(uid, out var battery))
            return false;

        current = battery.CurrentCharge;
        max = battery.MaxCharge;
        return true;
    }

    public bool HasCapability(EntityUid uid, CircuitCapability capability, CircuitAssemblyComponent? component = null)
    {
        return Resolve(uid, ref component, false) && component.Capabilities.HasFlag(capability);
    }

    public bool TryGetCpu(EntityUid uid, out EntityUid cpu, CircuitAssemblyComponent? component = null)
    {
        if (Resolve(uid, ref component, false) && component.Cpu is { } cpuUid)
        {
            cpu = cpuUid;
            return true;
        }

        cpu = default;
        return false;
    }

    public void StopMovement(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.WalkMode = CircuitWalkMode.Idle;
        component.WalkArrived = false;
        component.MoveDirection = Vector2.Zero;
        component.WalkTarget = null;
        component.LastMovePosition = Transform(uid).LocalPosition;
        _steering.Unregister(uid);

        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            mover.CurTickSprintMovement = Vector2.Zero;
            mover.WishDir = Vector2.Zero;
        }
    }

    private bool TryFindSlotForItem(
        EntityUid uid,
        EntityUid item,
        CircuitAssemblyComponent component,
        out string slotId)
    {
        slotId = string.Empty;

        if (HasComp<CircuitCpuComponent>(item))
            return TryFindEmptyRole(uid, component, CircuitSlotRole.Cpu, out slotId);

        if (HasComp<PowerCellComponent>(item))
            return TryFindEmptyRole(uid, component, CircuitSlotRole.Cell, out slotId);

        if (TryComp<CircuitPartComponent>(item, out var part))
        {
            var role = part.PartType switch
            {
                CircuitPartType.Locomotion => CircuitSlotRole.Locomotion,
                CircuitPartType.Manipulator => CircuitSlotRole.Manipulator,
                CircuitPartType.Voice => CircuitSlotRole.Voice,
                CircuitPartType.Sensor => CircuitSlotRole.Sensor,
                CircuitPartType.Analyzer => CircuitSlotRole.Analyzer,
                CircuitPartType.Radio => CircuitSlotRole.Radio,
                _ => CircuitSlotRole.Utility,
            };

            return TryFindEmptyRole(uid, component, role, out slotId);
        }

        if (HasComp<RefillableSolutionComponent>(item) && HasComp<DrainableSolutionComponent>(item))
            return TryFindEmptyRole(uid, component, CircuitSlotRole.Reagent, out slotId);

        return false;
    }

    private bool TryFindEmptyRole(
        EntityUid uid,
        CircuitAssemblyComponent component,
        CircuitSlotRole role,
        out string slotId)
    {
        foreach (var (candidateId, candidateRole) in component.Slots)
        {
            if (candidateRole != role)
                continue;

            if (_itemSlots.TryGetSlot(uid, candidateId, out var slot) && slot.Item == null)
            {
                slotId = candidateId;
                return true;
            }
        }

        slotId = string.Empty;
        return false;
    }

    public bool CanModify(EntityUid uid, EntityUid actor, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || !component.Locked)
            return true;

        if (!_player.TryGetSessionByEntity(actor, out var session))
            return false;

        if (_admin.IsAdmin(session, includeDeAdmin: true))
            return true;

        return component.OwnerUserId != null && component.OwnerUserId == session.UserId.ToString();
    }

    public bool TrySetLocked(EntityUid uid, EntityUid actor, bool locked, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) ||
            !_player.TryGetSessionByEntity(actor, out var session))
        {
            return false;
        }

        var userId = session.UserId.ToString();

        if (component.Locked && !CanModify(uid, actor, component))
            return false;

        if (locked)
        {
            if (component.OwnerUserId == null)
            {
                component.OwnerUserId = userId;
                component.OwnerName = session.Name;
            }
            else if (component.OwnerUserId != userId &&
                     !_admin.IsAdmin(session, includeDeAdmin: true))
            {
                return false;
            }
        }

        component.Locked = locked;
        return true;
    }

    public bool TryGetBeaker(EntityUid uid, int index, out EntityUid beaker, CircuitAssemblyComponent? component = null)
    {
        beaker = default;
        if (!Resolve(uid, ref component, false) || index < 1 || index > component.Beakers.Count)
            return false;

        if (component.Beakers[index - 1] is not { } beakerUid)
            return false;

        beaker = beakerUid;
        return true;
    }

    private static string GetHandId(string slotId)
    {
        return $"hand_{slotId}";
    }

    private static bool TryGetBeakerIndex(string slotId, out int index)
    {
        index = 0;
        if (!slotId.StartsWith("beaker", StringComparison.Ordinal))
            return false;

        if (!int.TryParse(slotId.AsSpan("beaker".Length), out index))
            return false;

        return index >= 1 && index <= 4;
    }

    private bool IsCircuitPart(EntityUid item)
    {
        return HasComp<CircuitCpuComponent>(item) ||
               HasComp<CircuitPartComponent>(item) ||
               HasComp<PowerCellComponent>(item) ||
               HasComp<RefillableSolutionComponent>(item) && HasComp<DrainableSolutionComponent>(item);
    }
}
