namespace FSharp.Wasm.Actors

/// What a supervisor does when a child dies.
type Strategy =
    /// only the child that died is started again
    | OneForOne
    /// the others are stopped and all are started again (children that depend on each other)
    | OneForAll

/// What a supervisor asks the world to do.
type SupervisorCommand<'Id> =
    | Start of 'Id
    | Stop of 'Id
    /// more restarts in the window than allowed: the children are stopped and the failure goes to whoever supervises this supervisor
    | GiveUp

/// A supervisor as data: its children and the restarts it has made lately.  It does not run anything; it says what has to be started and stopped.
type Supervisor<'Id when 'Id: equality> =
    { Strategy: Strategy
      Children: 'Id list
      /// at most this many restarts within `WindowMs` (OTP's restart intensity), or the supervisor gives up
      MaxRestarts: int
      WindowMs: int64
      /// the times of the restarts inside the window, newest first
      Restarts: int64 list
      GaveUp: bool }

module Supervisor =
    /// A supervisor of these children, and the commands that start them all.
    let create (strategy: Strategy) (maxRestarts: int) (windowMs: int64) (children: 'Id list) : Supervisor<'Id> * SupervisorCommand<'Id> list =
        { Strategy = strategy; Children = children; MaxRestarts = maxRestarts; WindowMs = windowMs; Restarts = []; GaveUp = false },
        children |> List.map Start

    /// A child exited at `now` (a clock in ms; only the differences matter).  The commands start it again, or all of them, or give up when this is one restart too many for the window;
    /// after that the supervisor ignores what happens to its children.
    let exited (now: int64) (id: 'Id) (sup: Supervisor<'Id>) : Supervisor<'Id> * SupervisorCommand<'Id> list =
        if sup.GaveUp || not (List.contains id sup.Children) then sup, []
        else
            let recent = now :: (sup.Restarts |> List.filter (fun t -> now - t < sup.WindowMs))
            if recent.Length > sup.MaxRestarts then
                { sup with Restarts = recent; GaveUp = true }, (sup.Children |> List.filter (fun c -> c <> id) |> List.map Stop) @ [ GiveUp ]
            else
                let commands =
                    match sup.Strategy with
                    | OneForOne -> [ Start id ]
                    | OneForAll -> (sup.Children |> List.filter (fun c -> c <> id) |> List.map Stop) @ (sup.Children |> List.map Start)
                { sup with Restarts = recent }, commands
