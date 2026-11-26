namespace FSharp.Wasm.Frame

open System.Collections.Generic

/// How the total of a group is made of its leaves along one axis.  A flow (revenue, an expense) sums over the months; a balance is the last month's, an opening balance
/// the first's; a rate is the average.
type Agg =
    | Sum
    | Avg
    | First
    | Last

/// A number the facts carry, and the formula of its totals along the rows and along the columns.
type Measure = { Name: string; OverRows: Agg; OverCols: Agg }

/// A formula over the measures of a cell, taken of the totals (the variance of a group is the variance of its totals).  A division by zero is `nan`, which `Format.amount` shows as a dash.
type Expr =
    | Num of float
    | M of string
    | Add of Expr * Expr
    | Sub of Expr * Expr
    | Mul of Expr * Expr
    | Div of Expr * Expr

/// Two hierarchies and the measures between them.  Every measure is a matrix of its leaves (rows × columns) kept as prefix sums, so the total of any row group over any
/// column group is four reads, whatever the size of the groups.  Floating-point sums of prefix sums are exact to the cent for totals up to about 10^11.
type Cube =
    { RowTree: Hierarchy
      ColTree: Hierarchy
      Measures: Measure[]
      Names: string[]
      /// (rows + 1) × (cols + 1), row-major, per measure
      Sums: float[][]
      Formulas: (string * Expr)[] }

module Cube =

    let private indexOf (names: string[]) (name: string) : int =
        let mutable at = -1
        for i in 0 .. names.Length - 1 do
            if at < 0 && names.[i] = name then at <- i
        at

    /// A formula that names a measure that is not there fails when the cube is built, not when a cell is read.
    let rec private check (names: string[]) (e: Expr) : unit =
        match e with
        | Num _ -> ()
        | M name -> if indexOf names name < 0 then failwith ("no measure called " + name)
        | Add(a, b) | Sub(a, b) | Mul(a, b) | Div(a, b) -> check names a; check names b

    /// The cube of the facts: `rowLeaf` and `colLeaf` say, per fact, the leaf it belongs to, and `values.[m]` carries its measure m.  One pass over the facts for each measure.
    /// `formulas` add measures that are computed from the others.
    let build (rows: Hierarchy) (cols: Hierarchy) (measures: Measure[]) (formulas: (string * Expr)[]) (rowLeaf: int[]) (colLeaf: int[]) (values: float[][]) : Cube =
        let nr = rows.Leaves
        let nc = cols.Leaves
        let w = nc + 1
        let baseNames = measures |> Array.map (fun m -> m.Name)
        for (_, e) in formulas do check baseNames e
        let sums =
            Array.init measures.Length (fun m ->
                let s : float[] = Array.zeroCreate ((nr + 1) * w)
                let v = values.[m]
                // the matrix of leaves, shifted by a row and a column of zeros ...
                for i in 0 .. rowLeaf.Length - 1 do
                    let at = (rowLeaf.[i] + 1) * w + colLeaf.[i] + 1
                    s.[at] <- s.[at] + v.[i]
                // ... and then the prefix sums in place: the run of the row, plus the finished row above
                for r in 1 .. nr do
                    let mutable run = 0.0
                    for c in 1 .. nc do
                        run <- run + s.[r * w + c]
                        s.[r * w + c] <- run + s.[(r - 1) * w + c]
                s)
        { RowTree = rows
          ColTree = cols
          Measures = measures
          Names = Array.append baseNames (formulas |> Array.map fst)
          Sums = sums
          Formulas = formulas }

    /// The number of measures that carry numbers of their own (the ones after them are formulas).
    let measureCount (c: Cube) : int = c.Measures.Length

    /// The index of a measure or formula by name, -1 when there is none.
    let measureIndex (c: Cube) (name: string) : int = indexOf c.Names name

    let private total (c: Cube) (m: int) (rowNode: int) (colNode: int) : float =
        let r = c.RowTree.Nodes.[rowNode]
        let k = c.ColTree.Nodes.[colNode]
        let me = c.Measures.[m]
        // the leaves that make the group's total: all of them for a sum and an average, the first or the last one for an opening or a closing balance
        let rlo = (match me.OverRows with Last -> r.Hi - 1 | _ -> r.Lo)
        let rhi = (match me.OverRows with First -> r.Lo + 1 | _ -> r.Hi)
        let clo = (match me.OverCols with Last -> k.Hi - 1 | _ -> k.Lo)
        let chi = (match me.OverCols with First -> k.Lo + 1 | _ -> k.Hi)
        let w = c.ColTree.Leaves + 1
        let s = c.Sums.[m]
        let sum = s.[rhi * w + chi] - s.[rlo * w + chi] - s.[rhi * w + clo] + s.[rlo * w + clo]
        let overRows = if me.OverRows = Avg then sum / float (r.Hi - r.Lo) else sum
        if me.OverCols = Avg then overRows / float (k.Hi - k.Lo) else overRows

    let rec private evaluate (c: Cube) (e: Expr) (rowNode: int) (colNode: int) : float =
        match e with
        | Num x -> x
        | M name -> total c (indexOf c.Names name) rowNode colNode
        | Add(a, b) -> evaluate c a rowNode colNode + evaluate c b rowNode colNode
        | Sub(a, b) -> evaluate c a rowNode colNode - evaluate c b rowNode colNode
        | Mul(a, b) -> evaluate c a rowNode colNode * evaluate c b rowNode colNode
        | Div(a, b) ->
            let d = evaluate c b rowNode colNode
            if d = 0.0 then nan else evaluate c a rowNode colNode / d

    /// The value of measure `m` (a measure of the facts, or a formula after them) for a row node and a column node: the total of that group of rows over that group of
    /// columns.  A leaf is its own total.
    let value (c: Cube) (m: int) (rowNode: int) (colNode: int) : float =
        if m < c.Measures.Length then total c m rowNode colNode
        else evaluate c (snd c.Formulas.[m - c.Measures.Length]) rowNode colNode

    /// One leaf cell of a measure of the facts (row leaf, column leaf), read back from the prefix sums.
    let leaf (c: Cube) (m: int) (row: int) (col: int) : float =
        let w = c.ColTree.Leaves + 1
        let s = c.Sums.[m]
        s.[(row + 1) * w + col + 1] - s.[row * w + col + 1] - s.[(row + 1) * w + col] + s.[row * w + col]

    /// The cube with leaf cells of a measure of the facts replaced by values of their own (a plan that overrides the budget of a month of an account): `(row leaf, column leaf, value)`.  A change
    /// of one leaf adds its difference to the prefix sums below and to the right of it, so the totals of every group that holds it move by exactly that, and the others are not touched.  The
    /// measure's sums are copied, the other measures' are shared.  A cell that is in `changes` twice is the last one.
    let withLeaves (c: Cube) (m: int) (changes: (int * int * float)[]) : Cube =
        let nr = c.RowTree.Leaves
        let nc = c.ColTree.Leaves
        let w = nc + 1
        let s = Array.copy c.Sums.[m]
        for (row, col, v) in changes do
            let w0 = w
            let delta = v - (s.[(row + 1) * w0 + col + 1] - s.[row * w0 + col + 1] - s.[(row + 1) * w0 + col] + s.[row * w0 + col])
            for r in row + 1 .. nr do
                for k in col + 1 .. nc do
                    s.[r * w + k] <- s.[r * w + k] + delta
        { c with Sums = c.Sums |> Array.mapi (fun i x -> if i = m then s else x) }

/// A way to look at the facts: the leaf of each fact on one axis.
type Dimension = { Name: string; Tree: Hierarchy; Leaf: int[] }

/// The facts: `Count` of them, the dimensions they are placed on, and their measures (`Values.[m].[i]` is measure m of fact i).
type Facts =
    { Count: int
      Dimensions: Dimension[]
      Measures: Measure[]
      Values: float[][] }

module Facts =

    /// The cube with one dimension on the rows and another on the columns: turning a table is building it again, one pass over the facts.
    let pivot (f: Facts) (rowDim: int) (colDim: int) (formulas: (string * Expr)[]) : Cube =
        let r = f.Dimensions.[rowDim]
        let c = f.Dimensions.[colDim]
        Cube.build r.Tree c.Tree f.Measures formulas r.Leaf c.Leaf f.Values

    /// The facts under a cell: their positions, in the order they have.  One pass; for the lines behind a total.
    let entries (f: Facts) (rowDim: int) (rowNode: int) (colDim: int) (colNode: int) : int[] =
        let rd = f.Dimensions.[rowDim]
        let cd = f.Dimensions.[colDim]
        let r = rd.Tree.Nodes.[rowNode]
        let k = cd.Tree.Nodes.[colNode]
        let found = ResizeArray<int>()
        for i in 0 .. f.Count - 1 do
            let a = rd.Leaf.[i]
            if a >= r.Lo && a < r.Hi then
                let b = cd.Leaf.[i]
                if b >= k.Lo && b < k.Hi then found.Add i
        found.ToArray()
