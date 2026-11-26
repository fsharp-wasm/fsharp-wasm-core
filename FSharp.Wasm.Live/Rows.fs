namespace FSharp.Wasm.Live

open System.Collections.Generic
open FSharp.Wasm.Graph

/// Numeric columns by name, read inside a calc (so that the calc depends on them): the number of rows, a count that changes when something that was there changed or went (an append does not change
/// it), and a column as an array that holds at least that many numbers.  Whole numbers (the leaf of a hierarchy, a year, a category) are kept as the numbers they are, exactly up to 2^53.
type Columns =
    { Rows: unit -> int
      Epoch: unit -> int
      Column: string -> float[] }

module Columns =
    /// The columns of a node of a `Doc`: `names` are its column fields (all of the same length: the rows are those of the first).
    let ofDoc (doc: Doc) (id: int) (names: string[]) : Columns =
        { Rows = fun () -> doc.Count(id, names.[0])
          Epoch = fun () -> names |> Array.sumBy (fun n -> doc.Epoch(id, n))
          Column = fun name -> doc.Data(id, name) }

    /// Columns that are arrays and do not change.
    let ofArrays (named: (string * float[]) list) : Columns =
        let find (name: string) : float[] =
            match named |> List.tryFind (fun (n, _) -> n = name) with
            | Some (_, a) -> a
            | None -> failwith ("no column called " + name)
        { Rows = (fun () -> match named with (_, a) :: _ -> a.Length | [] -> 0)
          Epoch = (fun () -> 0)
          Column = find }

/// A choice of rows: the numbers of the rows that were chosen (`Identity`: every row, and `Index` is not used), and an `Epoch` that changes when what was chosen before changed or went, while a
/// selection that only grew keeps it, so that whoever kept a result of the rows before can add the new ones.
type Rows = { Index: int[]; Count: int; Epoch: int; Identity: bool }

module Rows =
    let empty : Rows = { Index = [||]; Count = 0; Epoch = 0; Identity = false }

    /// The row number of the k-th chosen row.
    let at (r: Rows) (k: int) : int = if r.Identity then k else r.Index.[k]

/// What a scan keeps between two runs.
type private Scan =
    { mutable Index: int[]
      mutable Count: int
      mutable Seen: int
      mutable Epoch: int
      mutable Generation: int
      mutable Version: int }

/// The accumulated groups of a run of `groupBy`.
type private Accumulator =
    { Slots: Dictionary<int, int>
      mutable Keys: int[]
      mutable Sizes: int[]
      mutable Values: float[][]
      mutable Groups: int
      mutable SeenRows: int
      mutable RowsEpoch: int
      mutable ColsEpoch: int }

/// What a measure of a group is made of (not the `Agg` of a cube, which says how a total is made along an axis): the number of rows, or a sum, smallest, largest or mean of a column.
type Stat =
    | Count
    | Sum
    | Min
    | Max
    | Mean

/// The groups of a table, in the order of their keys: the key of each, how many rows it has, and each measure (`Values.[m].[g]`).
type Groups = { Keys: int[]; Sizes: int[]; Values: float[][]; Count: int }

module Live =
    let private none (_: Rows) (_: Rows) : bool = false

    /// Every row of the columns, as a selection: it is another one when a row is added, and has a new epoch when something that was there changed.
    let all (engine: Engine) (cols: Columns) : Calc<Rows> =
        let mutable generation = 0
        let mutable last = -1
        Calc<Rows>(engine, (fun _ _ -> false), Rows.empty, (fun _ _ -> ()), fun () ->
            let n = cols.Rows()
            let e = cols.Epoch()
            if e <> last then
                generation <- generation + 1
                last <- e
            { Index = [||]; Count = n; Epoch = generation; Identity = true })

    /// The rows that pass a test.  `prepare` is told the parameter and the columns and gives the test of a row (it fetches the columns it needs once, not for each row).  A table that only grew
    /// costs the test of the new rows; a new parameter, or something that changed in the columns, is a scan of the whole.
    let filter (engine: Engine) (cols: Columns) (param: Source<'p>) (prepare: 'p -> Columns -> int -> bool) : Calc<Rows> =
        let st = { Index = Array.zeroCreate 64; Count = 0; Seen = 0; Epoch = -1; Generation = 0; Version = -1 }
        Calc<Rows>(engine, (fun _ _ -> false), Rows.empty, (fun _ _ -> ()), fun () ->
            let n = cols.Rows()
            let e = cols.Epoch()
            let p = param.Value
            let v = param.Version
            let test = prepare p cols
            if e <> st.Epoch || v <> st.Version || n < st.Seen then
                st.Count <- 0
                st.Seen <- 0
                st.Generation <- st.Generation + 1
                st.Epoch <- e
                st.Version <- v
            for i in st.Seen .. n - 1 do
                if test i then
                    if st.Count = st.Index.Length then
                        let grown : int[] = Array.zeroCreate (st.Index.Length * 2)
                        Array.blit st.Index 0 grown 0 st.Count
                        st.Index <- grown
                    st.Index.[st.Count] <- i
                    st.Count <- st.Count + 1
            st.Seen <- n
            { Index = st.Index; Count = st.Count; Epoch = st.Generation; Identity = false })

    let private grow (a: 'a[]) (need: int) (fill: 'a) : 'a[] =
        if need <= a.Length then a
        else
            let grown = Array.create (max need (a.Length * 2)) fill
            Array.blit a 0 grown 0 a.Length
            grown

    /// The rows of a selection grouped by the whole number in `key`: for each group the number of its rows and each of the `measures` (a column and how to make one number of it; the column of a
    /// `Count` is not read).  The groups are in the order of their keys.  A table that only grew costs the new rows.
    let groupBy (engine: Engine) (cols: Columns) (rows: unit -> Rows) (key: string) (measures: (string * Stat)[]) : Calc<Groups> =
        let acc =
            { Slots = Dictionary<int, int>(); Keys = Array.zeroCreate 16; Sizes = Array.zeroCreate 16
              Values = Array.init measures.Length (fun _ -> Array.zeroCreate 16); Groups = 0; SeenRows = 0; RowsEpoch = -1; ColsEpoch = -1 }
        let empty = { Keys = [||]; Sizes = [||]; Values = Array.init measures.Length (fun _ -> [||]); Count = 0 }
        Calc<Groups>(engine, (fun _ _ -> false), empty, (fun _ _ -> ()), fun () ->
            let sel = rows ()
            let e = cols.Epoch()
            let keyColumn = cols.Column key
            let columns = measures |> Array.map (fun (name, agg) -> if agg = Count then keyColumn else cols.Column name)
            if sel.Epoch <> acc.RowsEpoch || e <> acc.ColsEpoch || sel.Count < acc.SeenRows then
                acc.Slots.Clear()
                acc.Groups <- 0
                acc.SeenRows <- 0
                acc.RowsEpoch <- sel.Epoch
                acc.ColsEpoch <- e
            for k in acc.SeenRows .. sel.Count - 1 do
                let row = Rows.at sel k
                let g = int keyColumn.[row]
                let slot =
                    match acc.Slots.TryGetValue g with
                    | true, s -> s
                    | _ ->
                        let s = acc.Groups
                        acc.Groups <- s + 1
                        acc.Slots.[g] <- s
                        acc.Keys <- grow acc.Keys (s + 1) 0
                        acc.Sizes <- grow acc.Sizes (s + 1) 0
                        acc.Keys.[s] <- g
                        acc.Sizes.[s] <- 0
                        for m in 0 .. measures.Length - 1 do
                            acc.Values.[m] <- grow acc.Values.[m] (s + 1) 0.0
                            acc.Values.[m].[s] <- (match snd measures.[m] with Min -> infinity | Max -> -infinity | _ -> 0.0)
                        s
                acc.Sizes.[slot] <- acc.Sizes.[slot] + 1
                for m in 0 .. measures.Length - 1 do
                    let x = columns.[m].[row]
                    let a = acc.Values.[m]
                    match snd measures.[m] with
                    | Count -> a.[slot] <- a.[slot] + 1.0
                    | Sum | Mean -> a.[slot] <- a.[slot] + x
                    | Min -> if x < a.[slot] then a.[slot] <- x
                    | Max -> if x > a.[slot] then a.[slot] <- x
            acc.SeenRows <- sel.Count
            // in the order of the keys; a mean is the sum over the rows of the group
            let order = [| 0 .. acc.Groups - 1 |] |> Array.sortBy (fun s -> acc.Keys.[s])
            { Keys = order |> Array.map (fun s -> acc.Keys.[s])
              Sizes = order |> Array.map (fun s -> acc.Sizes.[s])
              Values =
                Array.init measures.Length (fun m ->
                    order |> Array.map (fun s -> match snd measures.[m] with Mean -> acc.Values.[m].[s] / float acc.Sizes.[s] | _ -> acc.Values.[m].[s]))
              Count = acc.Groups })
