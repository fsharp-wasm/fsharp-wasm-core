namespace FSharp.Wasm.Graph

open System.Collections.Generic

/// One step on the tape: what was done, by whom, the patch that undoes it, and what the engine did for it (how many calcs it reached, ran, and how many it left alone because they came out the same).
type Entry =
    { Number: int
      Label: string
      Actor: int
      /// it came from somewhere else
      Remote: bool
      Patch: Patch
      Inverse: Patch
      Flushed: Flushed }

/// The history of a `Doc` for one actor: every step is on the tape, the steps of this actor can be undone and done again (an undo is a new edit that restores what the step replaced, so it
/// travels like any other), and the steps that arrive from elsewhere are on the tape and leave the undo alone.
type Timeline(doc: Doc) =
    let tape = ResizeArray<Entry>()
    /// the patch that takes a step of this actor back, and the label of the step: what the tape says of an undo and of a redo
    let undo = Stack<Patch * string>()
    let redo = Stack<Patch * string>()

    let record (label: string) (remote: bool) (a: Applied) : unit =
        tape.Add { Number = tape.Count + 1; Label = label; Actor = a.Patch.Actor; Remote = remote; Patch = a.Patch; Inverse = a.Inverse; Flushed = a.Flushed }

    member _.Doc : Doc = doc
    member _.Tape : Entry[] = tape.ToArray()
    member _.CanUndo : bool = undo.Count > 0
    member _.CanRedo : bool = redo.Count > 0

    /// A step of this actor: it is on the tape and can be undone; what could be done again is gone.
    member _.Do(label: string, build: Tx -> unit) : Applied =
        let a = doc.Edit(label, build)
        record label false a
        undo.Push((a.Inverse, label))
        redo.Clear()
        a

    /// A patch made elsewhere: it is applied and on the tape, and it is not a step of this actor to undo.
    member _.Receive(patch: Patch) : Applied =
        let a = doc.Apply patch
        record patch.Label true a
        a

    /// Take back the last step of this actor.
    member _.Undo() : Applied option =
        if undo.Count = 0 then None
        else
            let (inverse, label) = undo.Pop()
            let a = doc.Apply inverse
            record ("undo " + label) false a
            redo.Push((a.Inverse, label))
            Some a

    /// Do again what was taken back.
    member _.Redo() : Applied option =
        if redo.Count = 0 then None
        else
            let (again, label) = redo.Pop()
            let a = doc.Apply again
            record ("redo " + label) false a
            undo.Push((a.Inverse, label))
            Some a
