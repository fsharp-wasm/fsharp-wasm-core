namespace FSharp.Wasm.Telemetry

/// The history of a tag, kept small.
module Historian =

    /// The readings worth keeping: the first and the last, every one that moved by more than `deadband` from the last one kept, a reading of another quality than the last kept, and one that came
    /// `maxGap` seconds after it (so that a flat line is still known to be alive).  `maxGap <= 0` keeps no such heartbeat.  What is left out lies within the deadband of the line through the
    /// kept readings, which is what a trend needs: an extreme larger than the deadband is never lost.
    let deadband (deadband: float) (maxGap: float) (samples: Sample[]) : Sample[] =
        if samples.Length <= 2 then samples
        else
            let kept = ResizeArray<Sample>()
            kept.Add samples.[0]
            for i in 1 .. samples.Length - 2 do
                let last = kept.[kept.Count - 1]
                let s = samples.[i]
                let moved = abs (s.Value - last.Value) > deadband
                let aged = maxGap > 0.0 && s.Time - last.Time >= maxGap
                if moved || aged || s.Quality <> last.Quality then kept.Add s
            kept.Add samples.[samples.Length - 1]
            kept.ToArray()
