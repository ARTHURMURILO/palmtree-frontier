# NSV13 - RBMK reactor console strings.
# Split out of the combined consoles.ftl so the RBMK and the stormdrive can be reviewed (and
# merged) independently: neither branch needs the other machine's strings.

rbmk-console-title = RBMK reactor control
rbmk-console-no-reactor = NO REACTOR LINKED. Check the console tag.
rbmk-console-status-running = CORE CRITICAL - REACTOR RUNNING
rbmk-console-status-idle = Reactor subcritical
rbmk-console-status-slagged = CORE DESTROYED - SLAGGED
rbmk-console-reactivity = Reactivity:
rbmk-console-reactivity-value = { $k } K
rbmk-console-temperature = Core temperature:
rbmk-console-temperature-value = { $value } °C
rbmk-console-pressure = Coolant pressure:
rbmk-console-pressure-value = { $value } PSI
rbmk-console-power = Output:
rbmk-console-power-value = { $value } %
rbmk-console-coolant-in = Coolant in:
rbmk-console-coolant-out = Coolant out:
rbmk-console-integrity = Vessel integrity:
rbmk-console-radiation = Emission:
rbmk-console-radiation-value = { $value } rad/s
rbmk-console-fuel-rods = Fuel rods:
rbmk-console-integrity-holding = Vessel holding. No structural losses this cycle.
rbmk-console-integrity-coolant = Vessel losing { $rate }/s - coolant feed at { $percent }% of demand.
rbmk-console-integrity-temperature = Vessel losing { $rate }/s - core above meltdown temperature.
rbmk-console-integrity-pressure = Vessel losing { $rate }/s - outlet above critical pressure.
rbmk-console-rod-setpoint = Control rod setpoint (0 - 3)
rbmk-console-rod-insertion = Rod insertion:
rbmk-console-rod-insertion-value = { $percent } % inserted
rbmk-console-mounted-rods = Mounted fuel rods
rbmk-console-rod-entry = { $name } - { $percent } % fuel
rbmk-console-eject = Eject
rbmk-console-eject-too-hot = Fuel handling refused: the reactor is above 20% power.
rbmk-console-tab-rods = Control rods
rbmk-console-tab-stats = Statistics
rbmk-console-tab-fuel = Fuel management
rbmk-console-rods-header = Control Rod Management:
rbmk-console-insertion = Control Rod Insertion:
rbmk-console-k = Neutrons per generation (K):
rbmk-console-target = Target criticality:
rbmk-console-target-value = { $value } K
rbmk-console-legend = Legend:
rbmk-console-legend-power = Reactor Power (%):
rbmk-console-legend-pressure = Reactor Pressure (PSI):
rbmk-console-legend-coolant = Coolant temperature (°C):
rbmk-console-legend-outlet = Outlet temperature (°C):
rbmk-console-no-fuel = No fuel rods loaded.
