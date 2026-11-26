namespace FSharp.Wasm.Telemetry

/// How far a reading can be trusted, as in OPC: a bad one comes from an instrument that has failed or is cut off, an uncertain one is a value that is there but not known to be right.
type Quality =
    | Good
    | Uncertain
    | Bad

/// One reading of a tag: when (seconds), what, and how far it can be trusted.
type Sample = { Time: float; Value: float; Quality: Quality }

/// The limits of an analog tag; a limit that is `None` is not watched.  `Deadband` is how far back across a limit the value must come before the alarm leaves that level (so that a value that
/// shivers on the limit raises one alarm and not a hundred); `Delay` is how many seconds the value must stay beyond a limit before the alarm is raised (0: at once).
type Limits =
    { LowLow: float option
      Low: float option
      High: float option
      HighHigh: float option
      Deadband: float
      Delay: float }

module Limits =
    /// No limits: nothing alarms (except a bad reading).
    let none : Limits = { LowLow = None; Low = None; High = None; HighHigh = None; Deadband = 0.0; Delay = 0.0 }

/// Where the value is against its limits.  `Fault` is a bad reading: the alarm that the number cannot be trusted.
type Level =
    | Normal
    | Low
    | LowLow
    | High
    | HighHigh
    | Fault

/// The life of an alarm as ISA-18.2 draws it: it is raised and unacknowledged, a person acknowledges it, and it is gone when the value is back; or the value is back before anyone saw it
/// (returned, still to be acknowledged).
type Lifecycle =
    | Clear
    | ActiveUnacknowledged
    | ActiveAcknowledged
    | ReturnedUnacknowledged

type EventKind =
    /// the alarm became active
    | Raised
    /// a more severe level than before: it has to be acknowledged again
    | Escalated
    /// a less severe level, still active
    | Eased
    /// the value is back and nobody had acknowledged the alarm
    | Returned
    /// the value is back and the alarm had been acknowledged: it is over
    | Cleared
    | Acknowledged

/// Something that happened to the alarm, for a log, a horn or a message.
type Event = { Time: float; Kind: EventKind; Level: Level; Value: float }

/// The state of the alarm of one tag.  `Pending` is a level that the value has reached and that waits for its delay; `Shelved` keeps the alarm from telling anyone (the state is still kept).
type State =
    { Level: Level
      Lifecycle: Lifecycle
      Since: float
      Pending: (Level * float) option
      Shelved: bool }

module Alarm =

    let initial : State = { Level = Normal; Lifecycle = Clear; Since = 0.0; Pending = None; Shelved = false }

    /// How serious a level is: the second level of a limit is more than the first, a fault more than both.
    let severity (l: Level) : int =
        match l with
        | Normal -> 0
        | Low | High -> 1
        | LowLow | HighHigh -> 2
        | Fault -> 3

    let isActive (s: State) : bool = s.Lifecycle = ActiveUnacknowledged || s.Lifecycle = ActiveAcknowledged

    /// What needs a person: active and not acknowledged, or returned and not acknowledged.
    let needsAttention (s: State) : bool = s.Lifecycle = ActiveUnacknowledged || s.Lifecycle = ReturnedUnacknowledged

    /// The level the value has, given the one it had: a limit is entered at the limit, and left only when the value is back by the deadband.
    let level (limits: Limits) (current: Level) (v: float) : Level =
        let above (limit: float option) (slack: float) = match limit with Some l -> v >= l - slack | None -> false
        let below (limit: float option) (slack: float) = match limit with Some l -> v <= l + slack | None -> false
        let db = limits.Deadband
        if above limits.HighHigh 0.0 then HighHigh
        elif below limits.LowLow 0.0 then LowLow
        elif current = HighHigh && above limits.HighHigh db then HighHigh
        elif current = LowLow && below limits.LowLow db then LowLow
        elif above limits.High 0.0 then High
        elif below limits.Low 0.0 then Low
        elif current = High && above limits.High db then High
        elif current = Low && below limits.Low db then Low
        else Normal

    let private event (time: float) (kind: EventKind) (level: Level) (value: float) : Event = { Time = time; Kind = kind; Level = level; Value = value }

    /// The alarm takes a new level: what the lifecycle becomes, and what to tell.
    let private move (s: State) (next: Level) (time: float) (value: float) : State * Event list =
        let was = severity s.Level
        let now = severity next
        let tell (kind: EventKind) = if s.Shelved then [] else [ event time kind next value ]
        if next = s.Level then ({ s with Pending = None }, [])
        elif now = 0 then
            // the value is back: over at once when someone had acknowledged it, else it waits for a person
            match s.Lifecycle with
            | ActiveAcknowledged -> ({ s with Level = Normal; Lifecycle = Clear; Since = time; Pending = None }, tell Cleared)
            | ActiveUnacknowledged -> ({ s with Level = Normal; Lifecycle = ReturnedUnacknowledged; Since = time; Pending = None }, tell Returned)
            | _ -> ({ s with Level = Normal; Since = time; Pending = None }, [])
        elif not (isActive s) then ({ s with Level = next; Lifecycle = ActiveUnacknowledged; Since = time; Pending = None }, tell Raised)
        elif now > was then ({ s with Level = next; Lifecycle = ActiveUnacknowledged; Since = time; Pending = None }, tell Escalated)
        else ({ s with Level = next; Since = time; Pending = None }, tell Eased)

    /// A reading arrives.  A bad one is a fault at once; a value that goes beyond a limit is raised after the delay, and one that comes back is eased or cleared at once.
    let step (limits: Limits) (s: State) (sample: Sample) : State * Event list =
        let target = if sample.Quality = Bad then Fault else level limits s.Level sample.Value
        if severity target <= severity s.Level || target = Fault || limits.Delay <= 0.0 then move s target sample.Time sample.Value
        else
            match s.Pending with
            | Some (l, since) when l = target ->
                if sample.Time - since >= limits.Delay then move s target sample.Time sample.Value else (s, [])
            | _ -> ({ s with Pending = Some (target, sample.Time) }, [])

    /// A person acknowledges the alarm.
    let acknowledge (time: float) (s: State) : State * Event list =
        let tell = if s.Shelved then [] else [ event time Acknowledged s.Level nan ]
        match s.Lifecycle with
        | ActiveUnacknowledged -> ({ s with Lifecycle = ActiveAcknowledged }, tell)
        | ReturnedUnacknowledged -> ({ s with Lifecycle = Clear; Since = time }, tell)
        | _ -> (s, [])

    /// Keep the alarm from telling anyone (a tag that is being worked on); its state goes on being kept.
    let shelve (s: State) : State = { s with Shelved = true }
    let unshelve (s: State) : State = { s with Shelved = false }

    /// The alarm of a tag over a run of readings: the state at the end and every event, in order.
    let run (limits: Limits) (samples: Sample[]) : State * Event list =
        let mutable s = initial
        let events = ResizeArray<Event>()
        for sample in samples do
            let (s', es) = step limits s sample
            s <- s'
            events.AddRange es
        (s, List.ofSeq events)
