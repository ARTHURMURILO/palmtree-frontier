# Palmtree: atmospherics devices ported from /tg/station.

# Temperature gate
gas-temperature-gate-ui-title = Temperature Gate
gas-temperature-gate-ui-status = Status
gas-temperature-gate-ui-status-on = On
gas-temperature-gate-ui-status-off = Off
gas-temperature-gate-ui-inlet = Inlet
gas-temperature-gate-ui-outlet = Outlet
gas-temperature-gate-ui-target = Target temperature
gas-temperature-gate-ui-temperature-unit = K
gas-temperature-gate-ui-set = Set
gas-temperature-gate-ui-set-max = Max
gas-temperature-gate-ui-set-to-current = Use current
gas-temperature-gate-ui-invert = Inverted (open when hotter)

gas-temperature-gate-examined-status = The gate is [color={$statusColor}]{$enabled ->
[true] open
*[false] closed
}[/color].
gas-temperature-gate-examined-temperature = It opens when the inlet gas is [color=lightblue]{$direction} than [color=lightblue]{$temperature} K[/color][/color].
gas-temperature-gate-examined-lower = colder
gas-temperature-gate-examined-higher = hotter
gas-temperature-gate-examined-flow-rate = The flow rate meter indicates [color=lightblue]{$flowRate} L/s[/color].

# Temperature pump
gas-temperature-pump-ui-title = Temperature Pump
gas-temperature-pump-ui-status = Status
gas-temperature-pump-ui-status-on = On
gas-temperature-pump-ui-status-off = Off
gas-temperature-pump-ui-inlet = Inlet (cooled)
gas-temperature-pump-ui-outlet = Outlet (heated)
gas-temperature-pump-ui-temperature-unit = K
gas-temperature-pump-ui-rate = Transfer rate
gas-temperature-pump-ui-set = Set
gas-temperature-pump-ui-set-max = Max
gas-temperature-pump-ui-heat-moved = Heat moved:
gas-temperature-pump-ui-heat-unit = kW

gas-temperature-pump-examined-status = The pump is [color={$statusColor}]{$enabled ->
[true] running
*[false] idle
}[/color].
gas-temperature-pump-examined-rate = It moves [color=lightblue]{$rate}%[/color] of the temperature difference per second.

# Atmos meter
gas-meter-examined-pressure = The gauge reads [color=lightblue]{$pressure} kPa[/color].
gas-meter-examined-temperature = The thermometer reads [color=lightblue]{$temperature} K[/color] ({$celsius} °C).

# Guidebook
guide-entry-temperaturedevices = Temperature devices

# Heat pump
gas-heat-pump-ui-title = Heat Pump
gas-heat-pump-ui-status = Status
gas-heat-pump-ui-status-on = On
gas-heat-pump-ui-status-off = Off
gas-heat-pump-ui-mode-cooling = Cooling
gas-heat-pump-ui-mode-heating = Heating
gas-heat-pump-ui-efficiency = Efficiency {$efficiency}%
gas-heat-pump-ui-gas = Gas port
gas-heat-pump-ui-waste = Waste port
gas-heat-pump-ui-temperature-unit = K
gas-heat-pump-ui-pressure-unit = kPa
gas-heat-pump-ui-target = Target temperature
gas-heat-pump-ui-set = Set
gas-heat-pump-ui-set-max = Max
gas-heat-pump-ui-set-to-current = Use current
gas-heat-pump-ui-safeties = Thermal safeties
gas-heat-pump-ui-moving = Heat moved
gas-heat-pump-ui-moving-amount = {$amount} kW
gas-heat-pump-ui-moving-none = 0 kW
gas-heat-pump-ui-no-waste = The waste port has no gas in it. There is nothing for the heat to move into.
gas-heat-pump-upgrade-heat-capacity = Heat capacity
gas-heat-pump-upgrade-heat-rate = Maximum heat moved
gas-heat-pump-upgrade-efficiency = Minimum efficiency
gas-heat-pump-upgrade-power = Power per heat moved
gas-heat-pump-upgrade-min-temperature = Minimum temperature
gas-heat-pump-upgrade-max-temperature = Maximum temperature
gas-heat-pump-ui-no-power = The heat pump has no power.

gas-heat-pump-examined-status = The heat pump is [color={$statusColor}]{$enabled ->
[true] running
*[false] off
}[/color].
gas-heat-pump-examined-mode = It is currently [color=lightblue]{$mode}[/color].
gas-heat-pump-examined-temperature = The thermostat is set to [color=lightblue]{$temperature} K[/color].
gas-heat-pump-examined-gas = Gas port: [color=lightblue]{$temperature} K[/color] at [color=lightblue]{$pressure} kPa[/color].
gas-heat-pump-examined-waste = Waste port: [color=lightblue]{$temperature} K[/color] at [color=lightblue]{$pressure} kPa[/color].
gas-heat-pump-examined-efficiency = It is running at [color=lightblue]{$efficiency}%[/color] efficiency.
gas-heat-pump-examined-safeties-off = The thermal safeties have been [color=red]tampered with[/color].
gas-heat-pump-mode-cooling = cooling
gas-heat-pump-mode-heating = heating
gas-heat-pump-safeties-tripped = The waste loop has reached critical levels, the heat pump shuts down!

guide-entry-heat-pump = Heat Pump
