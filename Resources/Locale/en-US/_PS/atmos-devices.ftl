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
gas-temperature-pump-ui-heat-unit = W

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
