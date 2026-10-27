using Content.Server._NF.Bank;
using Content.Server.CartridgeLoader;
using Content.Server._PS.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._PS.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Server.Containers;
using Robust.Shared.Timing;

namespace Content.Server._PS.CartridgeLoader.Cartridges;

/// <summary>
/// Palmtree: the RIX PDA cartridge. It lets a player transfer space roubles from their
/// Frontier bank account to another online player by account number, without visiting an ATM.
/// </summary>
public sealed class PSBankTransferCartridgeSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PSBankSystem _psBank = default!;

    private static readonly TimeSpan TransferCooldown = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PSBankTransferCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PSBankTransferCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnUiReady(Entity<PSBankTransferCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        ent.Comp.Result = string.Empty;
        ent.Comp.Success = false;
        UpdateUI(ent, args.Loader, FindHolder(args.Loader));
    }

    private void OnMessage(Entity<PSBankTransferCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not PSBankTransferUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        var actor = FindHolder(loader);

        if (actor == EntityUid.Invalid)
            actor = args.Actor;

        if (!Exists(actor) || !TryComp<BankAccountComponent>(actor, out _))
        {
            SetResult(ent, "no-sender-account", false);
            UpdateUI(ent, loader, actor);
            return;
        }

        if (_timing.CurTime < ent.Comp.NextTransfer)
        {
            SetResult(ent, "cooldown", false);
            UpdateUI(ent, loader, actor);
            return;
        }

        ent.Comp.NextTransfer = _timing.CurTime + TransferCooldown;

        if (_psBank.TryTransfer(actor, msg.TargetAccount, msg.Amount, out var error))
        {
            SetResult(ent, "success", true, ("amount", msg.Amount), ("account", msg.TargetAccount));
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), actor);
        }
        else
        {
            SetResult(ent, error, false, ("amount", msg.Amount), ("account", msg.TargetAccount));
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg"), actor);
        }

        UpdateUI(ent, loader, actor);
    }

    private void SetResult(Entity<PSBankTransferCartridgeComponent> ent, string key, bool success, params (string, object)[] args)
    {
        ent.Comp.Success = success;
        ent.Comp.Result = Loc.GetString($"rix-result-{key}", args);
    }

    private EntityUid FindHolder(EntityUid loader)
    {
        var current = loader;
        for (var i = 0; i < 10; i++)
        {
            if (HasComp<BankAccountComponent>(current))
                return current;

            if (!_container.TryGetContainingContainer(current, out var container))
                return EntityUid.Invalid;

            current = container.Owner;
        }

        return EntityUid.Invalid;
    }

    private void UpdateUI(Entity<PSBankTransferCartridgeComponent> ent, EntityUid loader, EntityUid actor)
    {
        var accountNumber = 0;
        var balance = 0;

        if (TryComp<BankAccountComponent>(actor, out var bank))
        {
            _psBank.EnsureAccountNumber((actor, bank));
            accountNumber = bank.AccountNumber;
        }

        if (actor.Valid)
            _bank.TryGetBalance(actor, out balance);

        var state = new PSBankTransferUiState(accountNumber, balance, ent.Comp.Result, ent.Comp.Success);
        _cartridge.UpdateCartridgeUiState(loader, state);
    }
}
