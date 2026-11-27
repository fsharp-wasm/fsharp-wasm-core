namespace FSharp.Wasm.Actors

// A note on the data structures: the workers and the results are lists here, where a `Map` would be the natural thing.  `Map<'K,'V>` as a record field is not yet compiled for WebAssembly GC
// (docs/STATUS.md of fsharp-wasm, issue fsharp-wasm#33); this is written with lists until that is fixed and goes back to `Map` then.  The cost: an answer is found among the results in O(results),
// which is fine for the hundreds of jobs a pool of a few workers has in flight at a time and not for a hundred thousand.

/// Where a worker is: asked to start and not up yet, up, or dead and waiting for its supervisor's decision.
type WorkerStatus =
    | Starting
    | Ready
    | Down

/// A worker as the pool sees it: the jobs in its mailbox (sent, not answered), and what it has done.
type Worker<'Job> =
    { Id: int
      Status: WorkerStatus
      /// the jobs in its mailbox, oldest first
      InFlight: (int * 'Job) list
      Done: int
      /// how often it was started again
      Restarts: int }

/// What the world tells the pool.
type PoolEvent<'Job, 'Result> =
    /// work to do: (job id, the job)
    | Submit of (int * 'Job) list
    /// a worker that was asked to start is up
    | Started of worker: int
    /// a worker answered a job (a late answer of a worker that was declared dead still counts, once)
    | Replied of worker: int * job: int * 'Result
    /// a worker died, at this time
    | Exited of worker: int * now: int64

/// What the pool asks the world to do.
type PoolCommand<'Job> =
    | StartWorker of int
    | StopWorker of int
    /// put this job in the mailbox of this worker
    | Dispatch of worker: int * job: int * 'Job
    /// the supervisor gave up: nothing more will run
    | GaveUp

/// A fault-tolerant pool of workers as a state machine.  Jobs are sent to workers with room in their mailbox (`Prefetch`); a worker that dies loses its mailbox and the jobs go back to the front
/// of the queue; the supervisor decides about restarting the worker.  A job is done once: the first answer counts, a second is ignored (at-least-once delivery, an idempotent end).
type Pool<'Job, 'Result> =
    { Supervisor: Supervisor<int>
      /// by id
      Workers: Worker<'Job> list
      Queue: (int * 'Job) list
      /// (job id, result), the newest first
      Results: (int * 'Result) list
      /// how many jobs there are altogether
      Total: int
      /// how many jobs a worker may have in its mailbox
      Prefetch: int
      Failed: bool }

module Pool =
    /// A pool of `workers` workers under a supervisor, and the commands that start them.
    let create (strategy: Strategy) (maxRestarts: int) (windowMs: int64) (prefetch: int) (workers: int) : Pool<'Job, 'Result> * PoolCommand<'Job> list =
        let ids = [ 1 .. workers ]
        let sup, _ = Supervisor.create strategy maxRestarts windowMs ids
        { Supervisor = sup
          Workers = ids |> List.map (fun i -> { Id = i; Status = Starting; InFlight = []; Done = 0; Restarts = 0 })
          Queue = []; Results = []; Total = 0; Prefetch = max 1 prefetch; Failed = false },
        ids |> List.map StartWorker

    /// The result of a job, once it has one.
    let result (job: int) (p: Pool<'Job, 'Result>) : 'Result option =
        p.Results |> List.tryFind (fun (j, _) -> j = job) |> Option.map snd

    let private hasResult (job: int) (p: Pool<'Job, 'Result>) : bool = p.Results |> List.exists (fun (j, _) -> j = job)

    /// Every job has its result.
    let isDone (p: Pool<'Job, 'Result>) : bool = p.Total > 0 && p.Results.Length = p.Total

    let private worker (id: int) (p: Pool<'Job, 'Result>) : Worker<'Job> option = p.Workers |> List.tryFind (fun w -> w.Id = id)
    let private setWorker (w: Worker<'Job>) (p: Pool<'Job, 'Result>) : Pool<'Job, 'Result> =
        { p with Workers = p.Workers |> List.map (fun x -> if x.Id = w.Id then w else x) }

    /// Hand out the queue: to the worker that is up with room and the shortest mailbox (the lowest id among them), one job at a time, so that the load is even and not the first worker's.
    let private fill (p: Pool<'Job, 'Result>) : Pool<'Job, 'Result> * PoolCommand<'Job> list =
        let rec go (p: Pool<'Job, 'Result>) (acc: PoolCommand<'Job> list) =
            match p.Queue with
            | [] -> p, List.rev acc
            | (jid, job) :: rest ->
                match p.Workers |> List.filter (fun w -> w.Status = Ready && w.InFlight.Length < p.Prefetch) with
                | [] -> p, List.rev acc
                | open' ->
                    let w = open' |> List.minBy (fun w -> w.InFlight.Length, w.Id)
                    go (setWorker { w with InFlight = w.InFlight @ [ (jid, job) ] } { p with Queue = rest }) (Dispatch(w.Id, jid, job) :: acc)
        if p.Failed then p, [] else go p []

    let private commandsOf (cs: SupervisorCommand<int> list) : PoolCommand<'Job> list =
        cs |> List.map (function Start i -> StartWorker i | Stop i -> StopWorker i | SupervisorCommand.GiveUp -> GaveUp)

    /// The pool after an event, and what the world has to do about it.
    let update (event: PoolEvent<'Job, 'Result>) (p: Pool<'Job, 'Result>) : Pool<'Job, 'Result> * PoolCommand<'Job> list =
        match event with
        | Submit jobs ->
            // a job id the pool already knows (done, waiting, or in a mailbox) is not a new job, and neither is a repeat inside this list
            let known (j: int) (extra: (int * 'Job) list) =
                hasResult j p || List.exists (fun (q, _) -> q = j) p.Queue || List.exists (fun (q, _) -> q = j) extra
                || (p.Workers |> List.exists (fun w -> List.exists (fun (q, _) -> q = j) w.InFlight))
            let fresh = jobs |> List.fold (fun acc (j, x) -> if known j acc then acc else acc @ [ (j, x) ]) []
            fill { p with Queue = p.Queue @ fresh; Total = p.Total + fresh.Length }
        | Started id ->
            match worker id p with
            | Some w when not p.Failed -> fill (setWorker { w with Status = Ready } p)
            | _ -> p, []
        | Replied(id, job, res) ->
            let without = p.Workers |> List.map (fun w -> { w with InFlight = w.InFlight |> List.filter (fun (j, _) -> j <> job) })
            if hasResult job p then
                // a second answer (the first came from a worker that was thought dead): the job is done once
                fill { p with Workers = without }
            else
                let credited = without |> List.map (fun w -> if w.Id = id && (p.Workers |> List.exists (fun x -> x.Id = id && List.exists (fun (j, _) -> j = job) x.InFlight)) then { w with Done = w.Done + 1 } else w)
                // the job may be in the queue again (its worker was declared dead): it is not sent a second time
                fill { p with Workers = credited; Results = (job, res) :: p.Results; Queue = p.Queue |> List.filter (fun (j, _) -> j <> job) }
        | Exited(id, now) ->
            match worker id p with
            | None -> p, []
            | Some w when w.Status = Down -> p, []              // already stopped (by one_for_all or a give-up): its exit changes nothing
            | Some w ->
                let sup, cs = Supervisor.exited now id p.Supervisor
                let started = cs |> List.choose (function Start i -> Some i | _ -> None)
                let stopped = cs |> List.choose (function Stop i -> Some i | _ -> None)
                // what a worker had in its mailbox goes back to the front of the queue, in the order it was sent; what is done already stays done.  With one_for_all the others lose theirs too.
                let lostBy (x: Worker<'Job>) = x.InFlight |> List.filter (fun (j, _) -> not (hasResult j p))
                let back = lostBy w @ (p.Workers |> List.filter (fun x -> List.contains x.Id stopped) |> List.collect lostBy)
                let workers =
                    p.Workers
                    |> List.map (fun x ->
                        if x.Id = id then { x with Status = Down; InFlight = []; Restarts = x.Restarts + 1 }
                        elif List.contains x.Id stopped then { x with Status = Down; InFlight = [] }
                        elif List.contains x.Id started then { x with Status = Starting }
                        else x)
                // the one that died is started again by its supervisor: Starting, not Down
                let workers = workers |> List.map (fun x -> if List.contains x.Id started then { x with Status = Starting } else x)
                let failed = cs |> List.contains SupervisorCommand.GiveUp
                let p', more = fill { p with Supervisor = sup; Workers = workers; Queue = back @ p.Queue; Failed = p.Failed || failed }
                p', commandsOf cs @ more
