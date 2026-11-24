# FSharp.Wasm.Actors

Processes the way Erlang/OTP has them, as pure state machines. Dated 2026-11-24; what was run that day is said as measured.

A supervisor and a pool of workers are **functions of an event and a state** that give a new state and **commands**. The world carries the commands out (a Web Worker, a thread, a test's scheduler)
and tells the state machine what happened. Nothing in the library starts a process or waits for one, so it runs under .NET and as WebAssembly GC, and a whole run can be replayed from its events.

## The pieces

- **`Supervisor`**: children, a strategy (`OneForOne`: only the one that died is started again; `OneForAll`: the others are stopped and all are started), and OTP's *restart intensity*: more
  than `MaxRestarts` restarts within `WindowMs` and the supervisor gives up (`GiveUp`, the children are stopped, the failure goes up). `Supervisor.exited now id sup` says what to start and stop.
- **`Pool`**: workers under a supervisor, a queue, a mailbox per worker (`Prefetch` jobs in flight), results.
  - `Submit` puts jobs in the queue, a job id that is known already (done, waiting, in a mailbox, or repeated in the list) is not a new job.
  - `Started` makes a worker `Ready`; the queue is handed out to the worker with room and the shortest mailbox.
  - `Replied` ends a job. The **first answer counts**; a later one (from a worker that was thought dead) changes nothing, and the job is not sent again. At-least-once delivery, an idempotent end.
  - `Exited` is a crash: the mailbox is lost, its jobs go back to the *front* of the queue in the order they were sent, the supervisor decides about the restart.
  - Commands: `StartWorker`, `StopWorker`, `Dispatch(worker, job, payload)`, `GaveUp`.

## What the tests check

`tests/FSharp.Wasm.Libs.Tests/ActorsTests.fs` simulates the world with a seeded generator: time, answers that take their time and arrive out of order, workers that crash with some probability
(a worker that died never delivers what it had on its way, as the message of a dead process does not). 120 seeds each:

- without trouble: every job has its result, once, from its own job; every worker is up and every mailbox empty; the workers' counts add up to the jobs;
- with 15 % of the dispatches ending in a crash, `OneForOne` and `OneForAll` (10 %): every job still has its result exactly once, nothing is left in a queue or a mailbox, every worker is up again, the pool did not give up;
- the restarts the supervisor made are the crashes that were delivered;
- every dispatch crashing, `MaxRestarts` 4: the pool gives up, is not done, and the simulation ends by itself (no endless restart loop);
- a second answer, a job submitted twice, the jobs of a dead worker going to the others, and a late answer of a worker thought dead.

89 tests (13 of them for this library), under .NET and as WebAssembly GC, 2026-11-24.

## What it is not, yet

- The workers and the results are **lists**, where `Map` would be the natural thing: `Map<'K,'V>` as a record field is not compiled for WebAssembly GC yet (fsharp-wasm#33), and a tuple key of `List.minBy` stops
  the compiler (fsharp-wasm#34; the test simulation takes the head of a sort). An answer is found among the results in O(results): fine for the hundreds of jobs a few workers have in flight, not for a hundred thousand.
  The library goes back to `Map` when the compiler does it.
- No behaviours with typed mailboxes (an actor as `state -> message -> state * effects` per process), no links and monitors between arbitrary processes, no hot code loading: the pool and the supervisor are the
  part a data-science job needs first (a fixed set of workers, jobs that may be lost, at-least-once with an idempotent end). The rest is built on the same idea when there is a use for it.
