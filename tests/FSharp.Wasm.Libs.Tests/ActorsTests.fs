/// The tests of FSharp.Wasm.Actors: the supervisor's strategies and its restart intensity, and the pool under a simulated world that crashes workers at random and delivers answers late
/// and out of order.  The properties are the ones that matter: every job has its result exactly once, nothing stays in a queue or a mailbox, a pool that cannot be kept alive gives up.
/// Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.ActorsTests

open Wasmecto
open FSharp.Wasm.Actors

// ── the supervisor ──
let supervisor =
    testList "Actors.Supervisor" [
        testCase "one_for_one starts only the child that died" <| fun () ->
            let sup, start = Supervisor.create OneForOne 3 1000L [ 1; 2; 3 ]
            Expect.equal start [ Start 1; Start 2; Start 3 ] "all children are started"
            let _, cs = Supervisor.exited 10L 2 sup
            Expect.equal cs [ Start 2 ] "only the second"
        testCase "one_for_all stops the others and starts all of them" <| fun () ->
            let sup, _ = Supervisor.create OneForAll 3 1000L [ 1; 2; 3 ]
            let _, cs = Supervisor.exited 10L 2 sup
            Expect.equal cs [ Stop 1; Stop 3; Start 1; Start 2; Start 3 ] "the others are stopped, then all are started"
        testCase "the restart that is one too many for the window makes the supervisor give up" <| fun () ->
            let sup, _ = Supervisor.create OneForOne 2 1000L [ 1 ]
            let sup1, c1 = Supervisor.exited 0L 1 sup
            let sup2, c2 = Supervisor.exited 100L 1 sup1
            let sup3, c3 = Supervisor.exited 200L 1 sup2
            Expect.equal (c1, c2) ([ Start 1 ], [ Start 1 ]) "two restarts are allowed"
            Expect.equal c3 [ GiveUp ] "the third within the window is not"
            Expect.isTrue sup3.GaveUp "and it has given up"
            Expect.equal (snd (Supervisor.exited 300L 1 sup3)) [] "after that it does nothing"
        testCase "restarts that are outside the window do not count" <| fun () ->
            let sup, _ = Supervisor.create OneForOne 1 1000L [ 1 ]
            let sup1, _ = Supervisor.exited 0L 1 sup
            let _, c = Supervisor.exited 5000L 1 sup1
            Expect.equal c [ Start 1 ] "the first restart is long ago"
        testCase "a child it does not have changes nothing" <| fun () ->
            let sup, _ = Supervisor.create OneForOne 1 1000L [ 1 ]
            Expect.equal (snd (Supervisor.exited 0L 9 sup)) [] "no such child"
    ]

// ── a simulated world: time, the answers of workers that take their time, workers that crash ──
type private Sim =
    { Pool: Pool<int, int>
      Now: int64
      /// (time, order, incarnation of the worker it is from, the event): what is on its way
      Events: (int64 * int * int * PoolEvent<int, int>) list
      /// (worker, incarnation): a list, because a Map in a record is not compiled yet (fsharp-wasm#33)
      Epochs: (int * int) list
      Order: int
      Seed: uint32
      Delivered: int
      Stopped: int list
      CrashPerMille: int }

let private nextSeed (s: uint32) : uint32 = s * 1664525u + 1013904223u
let private draw (m: int) (s: Sim) : int * Sim =
    let seed = nextSeed s.Seed
    int (seed >>> 8) % m, { s with Seed = seed }

let private epochOf (worker: int) (s: Sim) : int = match List.tryFind (fun (w, _) -> w = worker) s.Epochs with Some(_, e) -> e | None -> 0
let private bump (worker: int) (s: Sim) : Sim = { s with Epochs = (worker, 1 + epochOf worker s) :: (s.Epochs |> List.filter (fun (w, _) -> w <> worker)) }

let private schedule (delay: int64) (worker: int) (e: PoolEvent<int, int>) (s: Sim) : Sim =
    let epoch = epochOf worker s
    { s with Events = (s.Now + delay, s.Order, epoch, e) :: s.Events; Order = s.Order + 1 }

let private carryOut (cmds: PoolCommand<int> list) (s0: Sim) : Sim =
    cmds
    |> List.fold (fun (s: Sim) c ->
        match c with
        | StartWorker w ->
            let s = bump w s
            let d, s = draw 3 s
            schedule (int64 (1 + d)) w (Started w) s
        | StopWorker w -> { bump w s with Stopped = w :: s.Stopped }
        | Dispatch(w, j, job) ->
            let r, s = draw 1000 s
            let d, s = draw 5 s
            if r < s.CrashPerMille then schedule (int64 (1 + d)) w (Exited(w, s.Now + int64 (1 + d))) s
            else schedule (int64 (1 + d)) w (Replied(w, j, job * 2)) s
        | GaveUp -> s) s0

/// Deliver events in time order until none is left; an event from a worker's earlier life (it died since) never arrives, as the message of a dead process does not.
let rec private run (budget: int) (s: Sim) : Sim =
    match s.Events with
    | [] -> s
    | _ when budget = 0 -> s
    | events ->
        // the earliest by time, then by order
        let ((t, o, epoch, e) as first) = events |> List.minBy (fun (t, o, _, _) -> t, o)
        let rest = events |> List.filter (fun x -> x <> first)
        let worker = match e with Started w | Replied(w, _, _) | Exited(w, _) -> w | Submit _ -> 0
        let current = epochOf worker s
        if epoch <> current then run (budget - 1) { s with Events = rest }
        else
            let s = { s with Events = rest; Now = t; Delivered = s.Delivered + 1 }
            // a worker that crashes is a new incarnation from this moment: what it had on its way is lost
            let s = match e with Exited(w, _) -> bump w s | _ -> s
            let pool, cmds = Pool.update e s.Pool
            run (budget - 1) (carryOut cmds { s with Pool = pool })

let private simulate (strategy: Strategy) (workers: int) (prefetch: int) (jobs: int) (crashPerMille: int) (maxRestarts: int) (seed: int) : Sim =
    let pool, start = Pool.create strategy maxRestarts 100000L prefetch workers
    let s0 = { Pool = pool; Now = 0L; Events = []; Epochs = []; Order = 0; Seed = uint32 seed * 2654435761u + 12345u; Delivered = 0; Stopped = []; CrashPerMille = crashPerMille }
    let s1 = carryOut start s0
    let pool', cmds = Pool.update (Submit [ for j in 1 .. jobs -> j, j ]) s1.Pool
    run 200000 (carryOut cmds { s1 with Pool = pool' })

let private seeds = [ 1 .. 120 ]

let pool =
    testList "Actors.Pool" [
        testCase "a pool without trouble does every job once and ends with every worker up" <| fun () ->
            for seed in seeds do
                let s = simulate OneForOne 4 2 30 0 5 seed
                let p = s.Pool
                Expect.isTrue (Pool.isDone p) "done"
                Expect.equal (p.Results |> List.sortBy fst) [ for j in 1 .. 30 -> j, j * 2 ] "each job has its result, from the one job"
                Expect.isTrue (p.Workers |> List.forall (fun w -> w.Status = Ready && w.InFlight.IsEmpty)) "all up, nothing in a mailbox"
                Expect.equal (p.Workers |> List.sumBy (fun w -> w.Done)) 30 "the workers did thirty between them"
        testCase "with workers that crash, every job still has its result exactly once, one_for_one" <| fun () ->
            for seed in seeds do
                let s = simulate OneForOne 4 2 40 150 100000 seed
                let p = s.Pool
                Expect.isFalse p.Failed "not given up"
                Expect.isTrue (Pool.isDone p) (sprintf "seed %d: done" seed)
                Expect.equal (p.Results |> List.sortBy fst) [ for j in 1 .. 40 -> j, j * 2 ] (sprintf "seed %d: the results" seed)
                Expect.isTrue (p.Queue.IsEmpty && p.Workers |> List.forall (fun w -> w.InFlight.IsEmpty)) "no job is left anywhere"
                Expect.isTrue (p.Workers |> List.forall (fun w -> w.Status = Ready)) "every worker is up again"
        testCase "the same under one_for_all" <| fun () ->
            for seed in seeds do
                let s = simulate OneForAll 3 2 25 100 100000 seed
                Expect.isTrue (Pool.isDone s.Pool) (sprintf "seed %d: done" seed)
                Expect.equal (s.Pool.Results |> List.sortBy fst) [ for j in 1 .. 25 -> j, j * 2 ] (sprintf "seed %d: the results" seed)
        testCase "the restarts the supervisor made are the crashes that were delivered" <| fun () ->
            for seed in seeds do
                let s = simulate OneForOne 4 2 40 200 100000 seed
                Expect.equal (s.Pool.Workers |> List.sumBy (fun w -> w.Restarts)) s.Pool.Supervisor.Restarts.Length "one restart per crash"
        testCase "a pool whose workers all crash gives up, and does not run for ever" <| fun () ->
            for seed in seeds do
                let s = simulate OneForOne 3 2 20 1000 4 seed
                Expect.isTrue s.Pool.Failed (sprintf "seed %d: given up" seed)
                Expect.isFalse (Pool.isDone s.Pool) "and not done"
                Expect.isTrue s.Events.IsEmpty "nothing is left on its way: the simulation ended by itself"
        testCase "a second answer to a job is ignored, and a job submitted twice is one job" <| fun () ->
            let p, _ = Pool.create OneForOne 3 1000L 2 2
            let p, _ = Pool.update (Started 1) p
            let p, _ = Pool.update (Started 2) p
            let p, cs = Pool.update (Submit [ 1, 10; 1, 10; 2, 20 ]) p
            Expect.equal p.Total 2 "two jobs"
            Expect.equal (cs |> List.length) 2 "two dispatches"
            let p, _ = Pool.update (Replied(1, 1, 7)) p
            let p', cs' = Pool.update (Replied(2, 1, 99)) p
            Expect.equal (Pool.result 1 p') (Some 7) "the first answer counts"
            Expect.equal cs' [] "and nothing is sent again"
            Expect.equal p'.Results p.Results "the second changed nothing"
        testCase "the jobs of a worker that dies go to the front of the queue and to the others" <| fun () ->
            let p, _ = Pool.create OneForOne 3 1000L 3 2
            let p, _ = Pool.update (Started 1) p
            let p, cs = Pool.update (Submit [ 1, 10; 2, 20; 3, 30 ]) p
            Expect.equal (cs |> List.length) 3 "all three go to the only worker that is up"
            let p, _ = Pool.update (Started 2) p
            let p, cs = Pool.update (Exited(1, 5L)) p
            Expect.equal (cs |> List.filter (function StartWorker 1 -> true | _ -> false)) [ StartWorker 1 ] "the supervisor starts it again"
            Expect.equal (cs |> List.choose (function Dispatch(w, j, _) -> Some(w, j) | _ -> None)) [ (2, 1); (2, 2); (2, 3) ] "in the order they were sent, to the worker that is up"
        testCase "a late answer of a worker thought dead counts, and the job is not sent again" <| fun () ->
            let p, _ = Pool.create OneForOne 3 1000L 2 1
            let p, _ = Pool.update (Started 1) p
            let p, _ = Pool.update (Submit [ 1, 10 ]) p
            let p, _ = Pool.update (Exited(1, 5L)) p
            let p, _ = Pool.update (Replied(1, 1, 20)) p
            Expect.isTrue (Pool.isDone p) "done by the late answer"
            let _, cs = Pool.update (Started 1) p
            Expect.equal cs [] "and nothing is sent when the worker is up again"
    ]

let suites : Test list = [ supervisor; pool ]
