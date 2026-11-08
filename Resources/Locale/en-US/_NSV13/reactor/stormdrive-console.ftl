# NSV13 - stormdrive reactor console strings.
# Split out of the combined consoles.ftl, same reason as the RBMK file.

stormdrive-console-title = Stormdrive reactor control
stormdrive-console-no-reactor = NO REACTOR LINKED. Use a multitool on the console and then on the drive.
stormdrive-console-status-maintenance = MAINTENANCE MODE - lid open
stormdrive-console-status-idle = Reactor off-line, awaiting ignition
stormdrive-console-status-running = Fission reaction stable
stormdrive-console-status-meltdown = !! MELTDOWN IN PROGRESS !!
stormdrive-console-status-repair = Core destroyed - awaiting repair
stormdrive-console-status-reinforce = Repairs complete - awaiting reinforcement
stormdrive-console-status-refit = Reinforcement complete - awaiting refit
stormdrive-console-heat = Core temperature:
stormdrive-console-heat-value = { $value } °C
stormdrive-console-rod-integrity = Control rods:
stormdrive-console-percent-value = { $value } %
stormdrive-console-power = Output:
stormdrive-console-power-value = { $value } MW of { $max } MW
stormdrive-console-reaction-rate = Reaction rate:
stormdrive-console-countdown-value = { $seconds } s
stormdrive-console-meltdown-title = CORE DESTABILISED
stormdrive-console-meltdown-hint = Control rods have failed. Cool the core below the meltdown threshold before the fuse runs out.
stormdrive-console-maintenance = Engage maintenance
stormdrive-console-maintenance-off = Disengage maintenance
stormdrive-console-dump = Open fuel dump
stormdrive-console-dump-open = Close fuel dump
stormdrive-console-maintenance-refused = Maintenance may only be engaged while the reactor is off-line.
stormdrive-console-scram = SCRAM protocols engaged. Attempting reactor shutdown!
stormdrive-console-presets-header = Control presets:
stormdrive-console-az-1 = AZ-1 - FULLY RAISE CONTROL RODS
stormdrive-console-az-2 = AZ-2 - HIGH TEMPERATURE OPERATION
stormdrive-console-az-3 = AZ-3 - NOMINAL OPERATION
stormdrive-console-az-4 = AZ-4 - COLD START
stormdrive-console-az-5 = AZ-5 - SCRAM
stormdrive-console-az-6 = AZ-6 - MAINTENANCE MODE
stormdrive-console-az-7 = AZ-7 - FUEL DUMP
stormdrive-console-rod-header = Control Rod Insertion:
stormdrive-console-statistics = Statistics:
stormdrive-console-fuel-ratio = Fuel Ratio:
stormdrive-console-fuel-moles = Fuel Moles:
stormdrive-console-composition = Fuel Line Composition:
stormdrive-console-reaction-value = { $value } mol/s
stormdrive-console-moles-value = { $value } mol
stormdrive-console-gas-Plasma = Plasma: { $percent } %
stormdrive-console-gas-Tritium = Tritium: { $percent } %
stormdrive-console-gas-Oxygen = Oxygen: { $percent } %
stormdrive-console-gas-Nitrogen = Nitrogen: { $percent } %
stormdrive-console-gas-CarbonDioxide = Carbon Dioxide: { $percent } %
stormdrive-console-gas-WaterVapour = Water Vapour: { $percent } %
stormdrive-console-gas-NitrousOxide = Nitrous Oxide: { $percent } %
stormdrive-console-gas-Nucleium = Nucleium: { $percent } %
stormdrive-rods-title = Stormdrive control rod bay
stormdrive-rods-closed = Lid sealed. Engage maintenance to open the bay.
stormdrive-rods-open = Lid open. Rods may be mounted or pulled.
stormdrive-rods-broken = The bay is full of plutonium sludge. Clear it out first.
stormdrive-rods-header = Mounted control rods
stormdrive-rods-empty = No control rods mounted.
stormdrive-rods-entry = { $name } - { $percent } % integrity, x{ $effectiveness }
stormdrive-rods-eject-name = Eject { $name }
stormdrive-rods-full = The bay already holds the maximum number of control rods.
stormdrive-rods-no-bay = There is no intact rod bay left to open.
stormdrive-rods-not-maintenance = The lid is sealed.
    Engage maintenance protocols before opening the bay.
stormdrive-rods-mounted = The control rod locks into the coupling.
stormdrive-rods-live-blue-glow = A blue glow envelopes your hands!
stormdrive-repair-need-sheets = You need at least { $count } sheets of liner material to reline the reactor pit.
stormdrive-repair-sludge-spill = Some of the sludge spills onto the floor!
stormdrive-repair-relined = The reactor pit has been relined.
stormdrive-repair-welded = The reactor shielding has been reinforced.
stormdrive-repair-core-installed = The reactor core has been installed. The drive is factory-fresh.

# Stability readout and the diagnosis line under it. The tooltip is deliberately explicit: the
# score is otherwise invisible, and the whole point is that the fuel bars are the controls.
stormdrive-console-stability = Stability:
stormdrive-console-stability-tooltip = [bold]Reactor stability (0–100)[/bold]
    A hidden safety margin. The control rods do not touch it; only the fuel line does.
    • Fuel ratio ≥25 % and fuel moles under the amber line: [color=#21BA45]+0.5 per 2 s tick[/color], recovering.
    • Rich but over the amber line: the recovery is cancelled, -0.01 per tick.
    • Fuel ratio 12.5–25 %: -0.01 per tick. Over the line as well: -0.52 per tick.
    • Fuel below 12.5 %, or fewer moles than one tick of throughput: [color=#DB2828]-1 per tick[/color], starving.
    The amber line is 24 × the reaction rate in mol/s + 20 moles. The reactor ticks every 2 s and eats twice the mol/s figure each tick, so that is the "one tick of throughput" above.
    Below 75, every tick has a (100 - S)/4 % chance of a jolt: a coin flip between 10–16 °C per mol/s of reaction rate, or a fifth to a third more reaction rate. A heat jolt is checked against the meltdown line in the same tick, which is how a stable-looking core can suddenly arm a meltdown. Below 15, anomalies start spawning.
    Keep both fuel bars green. If stability is low, SCRAM and fix the mix; a full cooldown below 20 °C then re-igniting resets it to 100.
stormdrive-console-stability-holding = Stability holding at 100 %. Rich fuel mix, pressure nominal.
stormdrive-console-stability-steady = Stability steady. No fuel-line condition is moving it.
stormdrive-console-stability-recovering = Recovering +{ $rate }/s. The rich fuel mix is healing the core.
stormdrive-console-stability-rich-overpressure = Bleeding { $rate }/s. Fuel line over pressure is cancelling the rich-mix recovery;
    lower input pressure or dump.
stormdrive-console-stability-lean = Bleeding { $rate }/s. Fuel ratio below 25 %; add plasma or tritium.
stormdrive-console-stability-lean-overpressure = Bleeding { $rate }/s. Fuel ratio below 25 % AND the line is over pressure; fix both.
stormdrive-console-stability-starved = Collapsing { $rate }/s. The reaction is starved (fuel below 12.5 % or too few moles).
    Refuel now: heat falls, but stability is dying.
stormdrive-console-stability-overload = Losing { $rate }/s. A power overload is shaking the core apart; lower output.
stormdrive-console-stability-anomaly = Containment breach: an anomaly tore loose. Stability refunded +15.
stormdrive-console-status-hot = CORE ABOVE NOMINAL TEMPERATURE
stormdrive-console-status-overheat = CORE OVERHEATING - REDUCE OUTPUT
stormdrive-console-status-near-meltdown = CORE AT MELTDOWN THRESHOLD - SCRAM NOW
stormdrive-console-status-destabilised = CORE DESTABILISED - MELTDOWN FUSE ARMED

# Guidebook page name; the entry itself is Resources/Prototypes/_NSV13/Guidebook/stormdrive.yml.
guide-entry-stormdrive = Stormdrive
