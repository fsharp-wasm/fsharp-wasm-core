/// The tests of FSharp.Wasm.Telemetry: the level of a value against limits with a deadband, the life of an alarm (raised, acknowledged, returned, cleared, escalated, eased), the delay, a bad
/// reading, shelving, a value that shivers on its limit, and the compression of a history.  Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.TelemetryTests

open Wasmecto
open FSharp.Wasm.Telemetry

let private at (t: float) (v: float) : Sample = { Time = t; Value = v; Quality = Good }
let private kinds (es: Event list) : string = es |> List.map (fun e -> string e.Kind) |> String.concat ","
let private limits : Limits = { Limits.none with High = Some 80.0; HighHigh = Some 90.0; Low = Some 20.0; LowLow = Some 10.0; Deadband = 2.0 }

/// A run of readings through the alarm, one at a time: the state and the events of each.
let private feed (l: Limits) (readings: Sample list) : State * Event list =
    let mutable s = Alarm.initial
    let mutable all = []
    for r in readings do
        let (s', es) = Alarm.step l s r
        s <- s'
        all <- all @ es
    (s, all)

let level =
    testList "Telemetry.Level" [
        testCase "a limit is entered at the limit and left when the value is back by the deadband" <| fun () ->
            let l = { Limits.none with High = Some 80.0; Deadband = 5.0 }
            let at' (current: Level) (v: float) = Alarm.level l current v
            Expect.equal (at' Normal 79.9) Normal "below"
            Expect.equal (at' Normal 80.0) High "at the limit"
            Expect.equal (at' High 76.0) High "back, but not by the deadband"
            Expect.equal (at' High 74.9) Normal "back by the deadband"
        testCase "the second limit is more than the first, and a value that comes down passes through the first" <| fun () ->
            let at' (current: Level) (v: float) = Alarm.level limits current v
            Expect.equal (at' Normal 85.0) High "the first"
            Expect.equal (at' High 91.0) HighHigh "the second"
            Expect.equal (at' HighHigh 89.0) HighHigh "held by the deadband"
            Expect.equal (at' HighHigh 87.0) High "down to the first"
            Expect.equal (at' High 70.0) Normal "and out"
            Expect.equal (at' Normal 15.0) Low "the low side"
            Expect.equal (at' Low 5.0) LowLow "the second low"
            Expect.equal (at' LowLow 11.0) LowLow "held"
            Expect.equal (at' LowLow 13.0) Low "down to the first low"
        testCase "a limit that is not set is not watched" <| fun () ->
            Expect.equal (Alarm.level Limits.none Normal 1.0e9) Normal "nothing alarms"
            Expect.equal (Alarm.level { Limits.none with High = Some 5.0 } Normal -1.0e9) Normal "a high limit does not watch the low side"
    ]

let alarm =
    testList "Telemetry.Alarm" [
        testCase "the life of an alarm: raised, acknowledged, cleared" <| fun () ->
            let (s, es) = feed limits [ at 0.0 50.0; at 1.0 85.0 ]
            Expect.equal (s.Level, s.Lifecycle) (High, ActiveUnacknowledged) "raised"
            Expect.equal (kinds es) "Raised" "told once"
            Expect.isTrue (Alarm.needsAttention s) "a person is needed"
            let (s2, es2) = Alarm.acknowledge 2.0 s
            Expect.equal s2.Lifecycle ActiveAcknowledged "acknowledged"
            Expect.equal (kinds es2) "Acknowledged" "told"
            Expect.isFalse (Alarm.needsAttention s2) "nobody needed"
            let (s3, es3) = Alarm.step limits s2 (at 3.0 50.0)
            Expect.equal (s3.Level, s3.Lifecycle) (Normal, Clear) "the value is back: over"
            Expect.equal (kinds es3) "Cleared" "told"
        testCase "a value that came back before anyone saw it waits for a person" <| fun () ->
            let (s, es) = feed limits [ at 0.0 85.0; at 1.0 50.0 ]
            Expect.equal s.Lifecycle ReturnedUnacknowledged "returned, not acknowledged"
            Expect.equal (kinds es) "Raised,Returned" "told both"
            Expect.isTrue (Alarm.needsAttention s) "still needs a person"
            let (s2, _) = Alarm.acknowledge 2.0 s
            Expect.equal s2.Lifecycle Clear "now it is over"
        testCase "a more severe level has to be acknowledged again, a less severe one does not" <| fun () ->
            let (s, _) = feed limits [ at 0.0 85.0 ]
            let (acked, _) = Alarm.acknowledge 1.0 s
            let (s2, es2) = Alarm.step limits acked (at 2.0 95.0)
            Expect.equal (s2.Level, s2.Lifecycle) (HighHigh, ActiveUnacknowledged) "escalated: unacknowledged again"
            Expect.equal (kinds es2) "Escalated" "told"
            let (acked2, _) = Alarm.acknowledge 3.0 s2
            let (s3, es3) = Alarm.step limits acked2 (at 4.0 85.0)
            Expect.equal (s3.Level, s3.Lifecycle) (High, ActiveAcknowledged) "eased: still acknowledged"
            Expect.equal (kinds es3) "Eased" "told"
        testCase "a value that shivers on its limit raises one alarm with a deadband, and many without" <| fun () ->
            let wobble = [ for i in 0 .. 99 -> at (float i) (if i % 2 = 0 then 80.5 else 79.5) ]
            let (_, withBand) = feed limits wobble
            Expect.equal (kinds withBand) "Raised" "one alarm"
            let (_, without) = feed { limits with Deadband = 0.0 } wobble
            Expect.isTrue (without.Length > 50) "a hundred readings, an alarm for each swing"
        testCase "the delay: the value must stay beyond the limit, and a dip starts it again" <| fun () ->
            let delayed = { limits with Delay = 5.0 }
            let (s, es) = feed delayed [ at 0.0 85.0; at 3.0 86.0 ]
            Expect.equal (s.Level, es.Length) (Normal, 0) "not yet"
            Expect.isTrue s.Pending.IsSome "waiting"
            let (s2, es2) = feed delayed [ at 0.0 85.0; at 2.0 50.0; at 3.0 85.0; at 7.0 85.0 ]
            Expect.equal (s2.Level, es2.Length) (Normal, 0) "the dip started it again: only 4 seconds"
            let (s3, es3) = feed delayed [ at 0.0 85.0; at 3.0 86.0; at 5.0 84.0 ]
            Expect.equal (s3.Level, kinds es3) (High, "Raised") "five seconds beyond the limit"
        testCase "a bad reading is a fault at once, and the value counts again when it is good" <| fun () ->
            let bad = { Time = 1.0; Value = 0.0; Quality = Bad }
            let (s, es) = feed { limits with Delay = 5.0 } [ at 0.0 50.0; bad ]
            Expect.equal (s.Level, kinds es) (Fault, "Raised") "a fault, with no delay"
            let (s2, es2) = Alarm.step limits s (at 2.0 50.0)
            Expect.equal (s2.Level, s2.Lifecycle) (Normal, ReturnedUnacknowledged) "the instrument is back"
            Expect.equal (kinds es2) "Returned" "told"
        testCase "a shelved alarm tells nobody and keeps its state" <| fun () ->
            let shelved = Alarm.shelve Alarm.initial
            let (s, es) = Alarm.step limits shelved (at 0.0 95.0)
            Expect.equal (s.Level, s.Lifecycle, es.Length) (HighHigh, ActiveUnacknowledged, 0) "active, and silent"
            let (s2, es2) = Alarm.step limits (Alarm.unshelve s) (at 1.0 50.0)
            Expect.equal (kinds es2) "Returned" "told again once it is not shelved"
            Expect.equal s2.Lifecycle ReturnedUnacknowledged "and the state was kept"
        testCase "a run of readings gives the end state and every event in order" <| fun () ->
            let (s, es) = Alarm.run limits [| at 0.0 50.0; at 1.0 85.0; at 2.0 95.0; at 3.0 85.0; at 4.0 50.0 |]
            Expect.equal (kinds es) "Raised,Escalated,Eased,Returned" "in order"
            Expect.equal s.Lifecycle ReturnedUnacknowledged "at the end"
            Expect.equal (es |> List.map (fun e -> e.Time)) [ 1.0; 2.0; 3.0; 4.0 ] "when"
    ]

let historian =
    testList "Telemetry.Historian" [
        testCase "a flat line is kept as its ends, a spike is kept, and a heartbeat shows it is alive" <| fun () ->
            let flat = [| for i in 0 .. 99 -> at (float i) 10.0 |]
            let ends = Historian.deadband 0.5 0.0 flat
            Expect.equal ends.Length 2 "the first and the last"
            let spike = Array.copy flat
            spike.[50] <- at 50.0 30.0
            let kept = Historian.deadband 0.5 0.0 spike
            Expect.equal (kept |> Array.map (fun s -> s.Value)) [| 10.0; 30.0; 10.0; 10.0 |] "the spike and the reading after it that came back"
            let beat = Historian.deadband 0.5 25.0 flat
            Expect.equal (beat |> Array.map (fun s -> s.Time)) [| 0.0; 25.0; 50.0; 75.0; 99.0 |] "one every 25 seconds, and the last"
        testCase "a ramp is kept at each step of the deadband, and a change of quality is always kept" <| fun () ->
            let ramp = [| for i in 0 .. 20 -> at (float i) (float i) |]
            let steps = Historian.deadband 4.5 0.0 ramp
            Expect.equal (steps |> Array.map (fun s -> s.Value)) [| 0.0; 5.0; 10.0; 15.0; 20.0 |] "a reading at each step, and the last"
            let q = [| at 0.0 1.0; at 1.0 1.0; { Time = 2.0; Value = 1.0; Quality = Uncertain }; at 3.0 1.0; at 4.0 1.0 |]
            Expect.equal (Historian.deadband 1.0 0.0 q |> Array.map (fun s -> s.Time)) [| 0.0; 2.0; 3.0; 4.0 |] "both changes of quality"
    ]

let suites = [ level; alarm; historian ]
