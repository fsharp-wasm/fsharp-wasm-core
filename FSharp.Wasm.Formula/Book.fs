namespace FSharp.Wasm.Formula

open System.Collections.Generic
open FSharp.Wasm.Graph

/// A cell whose value changed.
type Change = { Cell: CellRef; Before: Value; After: Value }

/// What a text means, parsed once per text: a formula, or the number, the text or the error it is.
type private Meaning = { Text: string; Formula: Expr option; Constant: Value }

module private Meaning =
    let ofText (src: string) : Meaning =
        let typed = src.Trim()
        if typed = "" then { Text = src; Formula = None; Constant = Blank }
        elif typed.StartsWith "=" then
            match Parser.parse typed with
            | Ok e -> { Text = src; Formula = Some e; Constant = Blank }
            | Error _ -> { Text = src; Formula = None; Constant = Err Syntax }
        else
            let v = match FSharp.Wasm.Data.Numbers.parseFloat typed with Some n -> Value.Number n | None -> Value.Text typed
            { Text = src; Formula = None; Constant = v }

/// A cell of a sheet: the engine's calc that is its value, and what its text meant when it last ran.
type private SheetCell = { mutable Meaning: Meaning; mutable Value: Calc<Value> option }

/// The cells of a sheet as calcs of an engine: the value of a cell is a function of its text and of the cells (and names) its formula reads.  Where the text lives is not the sheet's business:
/// `text` is read inside the calc, so what it reads (a source of its own, a field of a `Doc`) is what the cell depends on.  A formula reads other cells through the sheet, so the engine finds what
/// depends on what by running them: the cell and what depends on it run again, once each, after the cells they read, and only when something they read came out different.  Cells that read each other
/// have the value `#CYCLE!`, every one of the cycle, and are themselves again when it is broken.
///
/// A name or a function of the host (`Actual`, the plan's measures) is outside the engine: `Invalidate` and `Recalculate` say that what is behind them changed.
type Sheet(engine: Engine, text: CellRef -> string, resolve: CellRef -> string -> Value option, custom: string -> (Value[] -> Value) option) =
    let cells = Dictionary<int, SheetCell>()
    let mutable heard : Change -> unit = fun _ -> ()

    let sameValue (a: Value) (b: Value) : bool =
        match a, b with
        | Number x, Number y -> x = y || (x - x <> 0.0 && y - y <> 0.0)
        | _ -> a = b

    let rec cellAt (at: CellRef) : SheetCell =
        let k = CellRef.key at
        match cells.TryGetValue k with
        | true, c -> c
        | _ ->
            let c = { Meaning = Meaning.ofText ""; Value = None }
            cells.[k] <- c
            let env : Env = { Cell = (fun r -> if CellRef.valid r then (valueOf r) else Blank); Name = resolve; Custom = custom }
            let calc =
                Calc<Value>(engine, sameValue, Err Cycle, (fun before after -> heard { Cell = at; Before = before; After = after }), fun () ->
                    let src = text at
                    if src <> c.Meaning.Text then c.Meaning <- Meaning.ofText src
                    match c.Meaning.Formula with
                    | Some f -> Eval.eval env at f
                    | None -> c.Meaning.Constant)
            c.Value <- Some calc
            c

    and valueOf (at: CellRef) : Value =
        match (cellAt at).Value with
        | Some calc -> calc.Value
        | None -> Blank

    /// Say what to do with a value that changed: it is told in the order the cells ran, which is the order of the dependencies.
    member _.OnChange with set (f: Change -> unit) = heard <- f

    /// The value of a cell; inside a calc it is a dependency.
    member _.Read(at: CellRef) : Value = valueOf at

    /// The value of a cell, as a view reads it (no dependency).
    member _.Peek(at: CellRef) : Value =
        match (cellAt at).Value with
        | Some calc -> calc.Peek
        | None -> Blank

    /// The cell's formula, when its text is one that parses.
    member _.Formula(at: CellRef) : Expr option =
        // the meaning is the one the cell ran with: what the text is now is what it ran with once the engine has flushed
        (cellAt at).Meaning.Formula

    /// The cells that were read or made so far, as cells (a cell that nobody wrote into is among them when something read it).
    member _.Known : CellRef[] = [| for k in cells.Keys -> CellRef.ofKey k |]

    /// Say that what the host's names or functions stand for changed for these cells: they and what reads them run again at the next flush (`engine.Flush()`), and the cells that came out with
    /// another value are told by `OnChange`.
    member _.Invalidate(at: CellRef[]) : unit =
        for r in at do
            match cells.TryGetValue(CellRef.key r) with
            | true, c -> (match c.Value with Some calc -> calc.Invalidate() | None -> ())
            | _ -> ()

    /// Every cell again.
    member this.Recalculate() : unit = this.Invalidate this.Known

/// A sheet with the text of its cells of its own: cells that hold a number, text or a formula, what depends on what, and the recalculation of what a change reaches.
///
/// `Set` takes the text a person types: a number, `=` and a formula, or text.  It parses it, and recalculates the cell and then, in dependency order, only the cells that depend on it, directly or
/// through others.  The answer is the cells whose value changed (with what they were), which is what a view repaints and what travels to another place as the patch of this edit.  A value that
/// comes out the same is not a change.  Cells that depend on each other are #CYCLE!, and are so named by the cells of the cycle and not by an endless loop.
///
/// A name or a function of the host (`Actual`, the plan's measures) is outside the book: `Recalculate` and `Invalidate` say that the data behind them changed.
type Book(resolve: CellRef -> string -> Value option, custom: string -> (Value[] -> Value) option) =
    let engine = Engine()
    let texts = Dictionary<int, Source<string>>()
    let changes = ResizeArray<Change>()

    let textOf (at: CellRef) : Source<string> =
        let k = CellRef.key at
        match texts.TryGetValue k with
        | true, s -> s
        | _ ->
            let s = Source<string>(engine, "", Same.text)
            texts.[k] <- s
            s

    let sheet = Sheet(engine, (fun at -> (textOf at).Value), resolve, custom)
    do sheet.OnChange <- fun c -> changes.Add c

    let flush () : Change[] =
        changes.Clear()
        engine.Flush() |> ignore
        changes.ToArray()

    new() = Book((fun _ _ -> None), (fun _ -> None))

    /// What the cell shows.
    member _.Get(col: int, row: int) : Value = sheet.Peek(CellRef.create col row)

    /// What was typed into the cell ("" when nothing was).
    member _.Source(col: int, row: int) : string =
        match texts.TryGetValue(CellRef.key (CellRef.create col row)) with
        | true, s -> s.Peek
        | _ -> ""

    /// The cell's formula, when it has one.
    member _.Formula(col: int, row: int) : Expr option = sheet.Formula(CellRef.create col row)

    /// How many cells hold something.
    member _.Count : int = texts.Values |> Seq.filter (fun s -> s.Peek.Trim() <> "") |> Seq.length

    /// The cells that hold something.
    member _.Cells : CellRef[] = [| for kv in texts do if kv.Value.Peek.Trim() <> "" then yield CellRef.ofKey kv.Key |]

    /// Put what a person typed into a cell: the cell and what depends on it are recalculated, and the cells whose value changed are returned.  An empty text clears the cell.  A text that
    /// starts with `=` is a formula (one that does not parse is the cell's #ERROR!, and the text is kept to be corrected); one that is a number is a number; anything else is text.
    member _.Set(col: int, row: int, text: string) : Change[] =
        let at = CellRef.create col row
        if not (CellRef.valid at) then [||]
        else
            // the cell exists (as a blank) before it is written, so that what is written is a change of its value
            sheet.Peek at |> ignore
            if (textOf at).Set text then flush () else [||]

    /// Say that what the host's names or functions stand for changed for these cells: they and what reads them are calculated again.
    member _.Invalidate(cells: CellRef[]) : Change[] =
        sheet.Invalidate cells
        flush ()

    member this.Recalculate() : Change[] =
        sheet.Recalculate()
        flush ()
