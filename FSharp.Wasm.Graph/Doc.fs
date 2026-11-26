namespace FSharp.Wasm.Graph

open System.Collections.Generic

/// What a field holds at its smallest: nothing, a flag, a number, text, or the id of a node.  A list of atoms, a map of atoms and a column of numbers are fields of their own; an atom that is a
/// `Ref` makes the graph a graph (the node it names can be anything, also a list or a table).
type Atom =
    | Nil
    | Bool of bool
    | Num of float
    | Text of string
    | Ref of int

module Atom =
    let same (a: Atom) (b: Atom) : bool =
        match a, b with
        | Num x, Num y -> Same.float x y
        | _ -> a = b

    /// The text of an atom, for people and for a plain reading of a field.
    let toText (a: Atom) : string =
        match a with
        | Nil -> ""
        | Bool b -> if b then "true" else "false"
        | Num n -> if n = System.Math.Floor n && abs n < 1.0e15 then string (int64 n) else string n
        | Text s -> s
        | Ref id -> "#" + string id

/// The order of two edits of the same field: the later one wins, and of two at the same time the one of the higher actor.  A stamp is a clock that every actor moves forward past what it has seen (a
/// Lamport clock), and the actor.  `none` is "not stamped yet": the graph stamps the edit when it takes it.
module Stamp =
    let none : int64 = 0L
    let make (clock: int) (actor: int) : int64 = int64 clock * 1024L + int64 actor
    let clock (s: int64) : int = int (s / 1024L)
    let actor (s: int64) : int = int (s % 1024L)

/// One edit of the graph.  Every edit is data: it can be written down, sent and applied again, and its inverse is made when it is applied (the graph knows what it replaced).
///
/// `Set` and `Put` are last-writer-wins by stamp, so two edits made without seeing each other give the same value in any order.  A list is addressed by the item before the new one (`Insert`),
/// as a sequence CRDT does it, and items are never taken out of it, only marked (`Drop`, `Restore`).  A column grows (`Append`) or is written over (`Write`, `Truncate`): it has one writer.
type Op =
    | Create of id: int * kind: string
    | Delete of id: int
    | Revive of id: int
    | Set of id: int * field: string * value: Atom * stamp: int64
    | Put of id: int * field: string * key: string * value: Atom * stamp: int64
    | Insert of id: int * field: string * after: int64 * item: int64 * value: Atom
    | Drop of id: int * field: string * item: int64
    | Restore of id: int * field: string * item: int64
    | Append of id: int * field: string * values: float[]
    | Truncate of id: int * field: string * length: int
    | Write of id: int * field: string * at: int * values: float[]

/// A step of someone: what they did (`Ops`, in order) and a word for it (`Label`).
type Patch = { Actor: int; Label: string; Ops: Op list }

/// A value and the stamp of the edit that wrote it.
type internal Register = { Source: Source<Atom>; mutable Stamp: int64 }

/// A map: a register per key, and a count that changes whenever a key comes or goes.
type internal Entries = { Registers: Dictionary<string, Register>; Keys: Source<int> }

type internal Item = { Stamp: int64; mutable Atom: Atom; mutable Dead: bool }

/// A list in the order of the sequence CRDT, with the items that were taken out kept (marked), and a count that changes with every edit of the list.
type internal Items = { Items: ResizeArray<Item>; Version: Source<int> }

/// A column of numbers: `Count` of them in `Data` (which may be longer).  `Epoch` changes when something that was there changed or went (an append does not change it).
type internal Col = { mutable Data: float[]; mutable Count: int; mutable Epoch: int; Version: Source<int> }

type internal Field =
    | One of Register
    | Many of Entries
    | List of Items
    | Column of Col

type internal NodeState = { Id: int; mutable Kind: string; mutable Created: bool; Fields: Dictionary<string, Field>; Alive: Source<bool> }

/// What a patch did: the patch as it was taken (every edit stamped), the patch that undoes it, and what the flush of the engine reached.
type Applied = { Patch: Patch; Inverse: Patch; Flushed: Flushed }

/// The graph: nodes with fields, edited by patches, and the engine that keeps what is derived from it.
///
/// A node has a kind and fields: a register (`Set`), a map from text to atoms (`Put`), a list (`Insert`), or a column of numbers (`Append`).  A calc reads them through the `Doc` (`Get`, `Entry`,
/// `Keys`, `Items`, `Count`, `Data`) and so depends on exactly what it read.  An edit is `Edit` (a step of this actor) or `Apply` (a patch made elsewhere, or an inverse), and ends with one flush of
/// the engine.  A node is never forgotten (`Delete` marks it), an id is never made twice (the actor is part of it), and an edit that arrives for a node before its `Create` is kept for it.
type Doc(actor: int) =
    let engine = Engine()
    let nodes = Dictionary<int, NodeState>()
    let mutable clock = 0
    let mutable counter = 0

    let atoms () : Source<Atom> = Source<Atom>(engine, Nil, Atom.same)
    let counts () : Source<int> = Source<int>(engine, 0, Same.int)

    let node (id: int) : NodeState =
        match nodes.TryGetValue id with
        | true, n -> n
        | _ ->
            let n = { Id = id; Kind = ""; Created = false; Fields = Dictionary<string, Field>(); Alive = Source<bool>(engine, true, (fun a b -> a = b)) }
            nodes.[id] <- n
            n

    let register (id: int) (field: string) : Register =
        let n = node id
        match n.Fields.TryGetValue field with
        | true, One r -> r
        | _ ->
            let r = { Source = atoms (); Stamp = Stamp.none }
            n.Fields.[field] <- One r
            r

    let entries (id: int) (field: string) : Entries =
        let n = node id
        match n.Fields.TryGetValue field with
        | true, Many e -> e
        | _ ->
            let e = { Registers = Dictionary<string, Register>(); Keys = counts () }
            n.Fields.[field] <- Many e
            e

    let entry (e: Entries) (key: string) : Register =
        match e.Registers.TryGetValue key with
        | true, r -> r
        | _ ->
            let r = { Source = atoms (); Stamp = Stamp.none }
            e.Registers.[key] <- r
            r

    let items (id: int) (field: string) : Items =
        let n = node id
        match n.Fields.TryGetValue field with
        | true, List l -> l
        | _ ->
            let l = { Items = ResizeArray<Item>(); Version = counts () }
            n.Fields.[field] <- List l
            l

    let column (id: int) (field: string) : Col =
        let n = node id
        match n.Fields.TryGetValue field with
        | true, Column c -> c
        | _ ->
            let c = { Data = Array.zeroCreate 16; Count = 0; Epoch = 0; Version = counts () }
            n.Fields.[field] <- Column c
            c

    let tick () : int64 =
        clock <- clock + 1
        Stamp.make clock actor

    /// The clock moves past every stamp it has seen; an edit that is not stamped yet gets the next one.
    let stamped (stamp: int64) : int64 =
        if stamp = Stamp.none then tick ()
        else
            if Stamp.clock stamp > clock then clock <- Stamp.clock stamp
            stamp

    let bump (s: Source<int>) = s.Set(s.Peek + 1) |> ignore

    let indexOfItem (l: Items) (stamp: int64) : int =
        let mutable at = -1
        for i in 0 .. l.Items.Count - 1 do
            if at < 0 && l.Items.[i].Stamp = stamp then at <- i
        at

    /// Do one edit: the edit as it was taken (stamped), and the edits that undo it (none when it changed nothing).
    let apply (op: Op) : Op * Op list =
        match op with
        | Create (id, kind) ->
            let n = node id
            n.Kind <- kind
            if n.Created then (op, [])
            else
                n.Created <- true
                (op, [ Delete id ])
        | Delete id ->
            let n = node id
            if n.Alive.Peek then
                n.Alive.Set false |> ignore
                (op, [ Revive id ])
            else (op, [])
        | Revive id ->
            let n = node id
            if n.Alive.Peek then (op, [])
            else
                n.Alive.Set true |> ignore
                (op, [ Delete id ])
        | Set (id, field, value, stamp0) ->
            let stamp = stamped stamp0
            let r = register id field
            if stamp > r.Stamp then
                let before = r.Source.Peek
                r.Stamp <- stamp
                r.Source.Set value |> ignore
                (Set (id, field, value, stamp), [ Set (id, field, before, Stamp.none) ])
            else (Set (id, field, value, stamp), [])
        | Put (id, field, key, value, stamp0) ->
            let stamp = stamped stamp0
            let e = entries id field
            let r = entry e key
            if stamp > r.Stamp then
                let before = r.Source.Peek
                r.Stamp <- stamp
                r.Source.Set value |> ignore
                if (before = Nil) <> (value = Nil) then bump e.Keys
                (Put (id, field, key, value, stamp), [ Put (id, field, key, before, Stamp.none) ])
            else (Put (id, field, key, value, stamp), [])
        | Insert (id, field, after, item0, value) ->
            let item = stamped item0
            let l = items id field
            if indexOfItem l item >= 0 then (Insert (id, field, after, item, value), [])
            else
                let mutable at = if after = Stamp.none then 0 else (let i = indexOfItem l after in if i < 0 then l.Items.Count else i + 1)
                // an item that was put after the same one later goes first, with whatever hangs on it (those have later stamps still)
                while at < l.Items.Count && l.Items.[at].Stamp > item do at <- at + 1
                l.Items.Insert(at, { Stamp = item; Atom = value; Dead = false })
                bump l.Version
                (Insert (id, field, after, item, value), [ Drop (id, field, item) ])
        | Drop (id, field, item) ->
            let l = items id field
            let i = indexOfItem l item
            if i >= 0 && not l.Items.[i].Dead then
                l.Items.[i].Dead <- true
                bump l.Version
                (op, [ Restore (id, field, item) ])
            else (op, [])
        | Restore (id, field, item) ->
            let l = items id field
            let i = indexOfItem l item
            if i >= 0 && l.Items.[i].Dead then
                l.Items.[i].Dead <- false
                bump l.Version
                (op, [ Drop (id, field, item) ])
            else (op, [])
        | Append (id, field, values) ->
            let c = column id field
            let before = c.Count
            let need = before + values.Length
            if need > c.Data.Length then
                let grown : float[] = Array.zeroCreate (max need (c.Data.Length * 2))
                Array.blit c.Data 0 grown 0 before
                c.Data <- grown
            Array.blit values 0 c.Data before values.Length
            c.Count <- need
            bump c.Version
            (op, [ Truncate (id, field, before) ])
        | Truncate (id, field, length) ->
            let c = column id field
            if length >= c.Count || length < 0 then (op, [])
            else
                let removed = Array.sub c.Data length (c.Count - length)
                c.Count <- length
                c.Epoch <- c.Epoch + 1
                bump c.Version
                (op, [ Append (id, field, removed) ])
        | Write (id, field, at, values) ->
            let c = column id field
            if at < 0 || at + values.Length > c.Count then (op, [])
            else
                let before = Array.sub c.Data at values.Length
                Array.blit values 0 c.Data at values.Length
                c.Epoch <- c.Epoch + 1
                bump c.Version
                (op, [ Write (id, field, at, before) ])

    member _.Engine : Engine = engine
    member _.Actor : int = actor

    /// A new id for a node: a count of this actor's own, and the actor, so that two actors never make the same one.
    member _.NewId() : int =
        counter <- counter + 1
        counter * 1024 + actor

    /// A stamp for an edit of this actor, later than everything it has seen.
    member _.Tick() : int64 = tick ()

    // ---- reading: inside a calc, what is read is what the calc depends on ----

    member _.Exists(id: int) : bool =
        match nodes.TryGetValue id with
        | true, n -> n.Alive.Value
        | _ -> false

    member _.Kind(id: int) : string =
        match nodes.TryGetValue id with
        | true, n -> n.Kind
        | _ -> ""

    /// A register.
    member _.Get(id: int, field: string) : Atom = (register id field).Source.Value

    /// One entry of a map: the calc depends on that key only (also when the key is not there yet).
    member _.Entry(id: int, field: string, key: string) : Atom = (entry (entries id field) key).Source.Value

    /// The keys of a map that hold something, in order; the calc depends on which keys there are.
    member _.Keys(id: int, field: string) : string[] =
        let e = entries id field
        e.Keys.Value |> ignore
        [ for kv in e.Registers do
            if kv.Value.Source.Peek <> Nil then yield kv.Key ]
        |> List.sort
        |> Array.ofList

    /// The items of a list that are there, in order.
    member _.Items(id: int, field: string) : Atom[] =
        let l = items id field
        l.Version.Value |> ignore
        [| for it in l.Items do
             if not it.Dead then yield it.Atom |]

    /// How many numbers a column holds; the calc depends on the column.
    member _.Count(id: int, field: string) : int =
        let c = column id field
        c.Version.Value |> ignore
        c.Count

    /// The numbers of a column, as the array it keeps (it holds `Count` numbers and may be longer): do not write into it.
    member _.Data(id: int, field: string) : float[] =
        let c = column id field
        c.Version.Value |> ignore
        c.Data

    /// Changes whenever something of a column that was there changed or went; it does not change with an append.
    member _.Epoch(id: int, field: string) : int =
        let c = column id field
        c.Version.Value |> ignore
        c.Epoch

    // ---- writing ----

    /// Apply a patch (a patch made elsewhere, or an inverse): its edits in order, then one flush.  An edit that is not stamped is stamped now; an edit of a stamp that lost against a later one
    /// changes nothing.  The answer has the patch as it was taken, with every edit stamped: what another place needs to take the same step.
    member _.Apply(patch: Patch) : Applied =
        let taken = ResizeArray<Op>()
        let inverse = ResizeArray<Op>()
        for op in patch.Ops do
            let (done', undo) = apply op
            taken.Add done'
            for u in undo do inverse.Insert(0, u)
        let flushed = engine.Flush()
        { Patch = { patch with Ops = List.ofSeq taken }; Inverse = { Actor = actor; Label = "undo " + patch.Label; Ops = List.ofSeq inverse }; Flushed = flushed }

    /// One step of this actor: `build` says what to change, and every change takes effect at once (a read inside `build` sees the edits before it); the engine runs when `build` is done.
    member this.Edit(label: string, build: Tx -> unit) : Applied =
        let tx = Tx(this)
        build tx
        let flushed = engine.Flush()
        { Patch = { Actor = actor; Label = label; Ops = tx.Ops }; Inverse = { Actor = actor; Label = "undo " + label; Ops = tx.Inverse }; Flushed = flushed }

    /// Do one edit now, without a flush: for a `Tx`.
    member internal _.Do(op: Op) : Op * Op list = apply op

    /// The stamps of the items of a list that are there (for the edits that name an item).
    member internal _.Stamps(id: int, field: string) : int64[] =
        let l = items id field
        [| for it in l.Items do
             if not it.Dead then yield it.Stamp |]

/// The edits of one step: each is applied to the graph at once (so that a read sees it) and written down as an `Op` with its stamp.
and Tx(doc: Doc) =
    let ops = ResizeArray<Op>()
    let inverse = ResizeArray<Op>()
    let run (op: Op) =
        let (done', undo) = doc.Do op
        ops.Add done'
        for u in undo do inverse.Insert(0, u)

    member _.Ops : Op list = List.ofSeq ops
    member _.Inverse : Op list = List.ofSeq inverse

    /// A new node of a kind; its id.
    member _.Create(kind: string) : int =
        let id = doc.NewId()
        run (Create (id, kind))
        id

    member _.Delete(id: int) : unit = run (Delete id)
    member _.Set(id: int, field: string, value: Atom) : unit = run (Set (id, field, value, Stamp.none))
    member _.Put(id: int, field: string, key: string, value: Atom) : unit = run (Put (id, field, key, value, Stamp.none))

    /// Put an item into a list at a place (0 is the front, the length is the end).
    member _.Insert(id: int, field: string, at: int, value: Atom) : unit =
        let stamps = doc.Stamps(id, field)
        let after = if at <= 0 || stamps.Length = 0 then Stamp.none else stamps.[min at stamps.Length - 1]
        run (Insert (id, field, after, Stamp.none, value))

    /// Take the item at a place out of a list.
    member _.Remove(id: int, field: string, at: int) : unit =
        let stamps = doc.Stamps(id, field)
        if at >= 0 && at < stamps.Length then run (Drop (id, field, stamps.[at]))

    member _.Append(id: int, field: string, values: float[]) : unit = run (Append (id, field, values))
    member _.Write(id: int, field: string, at: int, values: float[]) : unit = run (Write (id, field, at, values))
    member _.Truncate(id: int, field: string, length: int) : unit = run (Truncate (id, field, length))
