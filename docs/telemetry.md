# FSharp.Wasm.Telemetry

The readings of instruments, for dashboards and SCADA: what a value is worth, when it alarms, and how a history is kept small.  It is plain data and pure functions (no clock, no
I/O), so the same code runs under .NET and as WebAssembly GC, and a gateway (OPC-UA, MQTT, Modbus, behind `fsharp-wasm-edge`) only has to deliver `Sample`s.  Written 2026-11-26.

## A reading

```fsharp
type Quality = Good | Uncertain | Bad                    // as in OPC: bad is a failed or cut-off instrument
type Sample  = { Time: float; Value: float; Quality: Quality }
```

## The alarm of a tag

`Limits` has up to four limits (`LowLow`, `Low`, `High`, `HighHigh`; one that is `None` is not watched), a **deadband** and a **delay**.

- `Alarm.level limits current value` is the level a value has, given the one it had: a limit is entered at the limit and left only when the value is back by the deadband.  A value that
  shivers on its limit raises one alarm, not a hundred (the test counts both).
- `Alarm.step limits state sample` takes a reading and gives the new `State` and the `Event`s to tell.  A level beyond a limit is raised after `Delay` seconds, and a dip starts the
  delay again; a value coming back is eased or cleared at once; a `Bad` reading is a `Fault` at once.
- The life of an alarm is the one of ISA-18.2: `Clear` → `ActiveUnacknowledged` → `ActiveAcknowledged` → `Clear`, or `ActiveUnacknowledged` → `ReturnedUnacknowledged` (the value came
  back before anyone saw it) → `Clear`.  A more severe level has to be acknowledged again; a less severe one does not.  `Alarm.needsAttention` is what a horn or a list asks.
- `Alarm.acknowledge`, `Alarm.shelve` / `unshelve` (a tag that is being worked on: its state is kept, nobody is told), and `Alarm.run` (the end state and every event of a run of readings).

## The historian

`Historian.deadband deadband maxGap samples` keeps the first and the last reading, every one that moved by more than `deadband` from the last one kept, every change of quality, and
(with `maxGap > 0`) a heartbeat so that a flat line is known to be alive.  What is left out lies within the deadband of the line through the kept readings, so an extreme larger than the
deadband is never lost.

## Where it goes

Instruments (gauge, tank, sparkline, lamp, KPI) and diagrams (process mimic, node-link) are drawn by `fsharp-wasm-graphics` from these values; the values travel as patches of the data
graph (`docs/data-graph.md`), so a dashboard of ten thousand tags re-draws what changed.
