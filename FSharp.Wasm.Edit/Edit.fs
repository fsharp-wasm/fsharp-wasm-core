/// The edits of a document as data.  A document is a tree of items (a flat list is a tree whose items have no children); an edit is one
/// operation on it, and every operation has an inverse, so that undo and redo are the same function and a history is a list of operations.
/// Gestures (a drag, typing in place) end in one of these operations; nothing here knows about pixels, the DOM or a canvas.
namespace FSharp.Wasm.Edit

/// Where an item sits: the child indices from the top-level list.  [2] is the third top-level item, [2; 0] its first child.
type Path = int list

/// How a document is a tree of items: an item's children, and a copy of it with other children.
type Shape<'item> =
    { Kids: 'item -> 'item list
      WithKids: 'item -> 'item list -> 'item }

/// A place to put an item, relative to the item at a path.  `Into` makes it the last child.
type Place =
    | Before of Path
    | After of Path
    | Into of Path

/// One edit.  `Remove` and `Replace` carry what they take away, which is what makes them invertible.
/// `Move (source, target)`: the item at `source` is taken out, and put at `target` as that path reads in the document WITHOUT it.
type Op<'item> =
    | Insert of at: Path * item: 'item
    | Remove of at: Path * item: 'item
    | Move of source: Path * target: Path
    | Replace of at: Path * before: 'item * after: 'item
    | Many of Op<'item> list

module Ops =

    let rec private nth (i: int) (xs: 'a list) : 'a option =
        match xs with
        | [] -> None
        | x :: rest -> if i < 0 then None elif i = 0 then Some x else nth (i - 1) rest

    let rec private insertIndex (i: int) (x: 'a) (xs: 'a list) : 'a list =
        match xs with
        | [] -> [ x ]
        | h :: t -> if i <= 0 then x :: xs else h :: insertIndex (i - 1) x t

    let rec private removeAt (i: int) (xs: 'a list) : 'a list =
        match xs with
        | [] -> []
        | h :: t -> if i = 0 then t else h :: removeAt (i - 1) t

    let rec private replaceAt (i: int) (x: 'a) (xs: 'a list) : 'a list =
        match xs with
        | [] -> []
        | h :: t -> if i = 0 then x :: t else h :: replaceAt (i - 1) x t

    /// Edit the list that holds the item at `path` (`f` gets that list and the item's index in it, and returns the new list and an item it took out).
    let rec private within (shape: Shape<'i>) (path: Path) (f: 'i list -> int -> ('i list * 'i option) option) (items: 'i list) : ('i list * 'i option) option =
        match path with
        | [] -> None
        | [ i ] -> f items i
        | i :: rest ->
            match nth i items with
            | None -> None
            | Some parent ->
                match within shape rest f (shape.Kids parent) with
                | None -> None
                | Some (kids, taken) -> Some (replaceAt i (shape.WithKids parent kids) items, taken)

    /// The item at a path.
    let get (shape: Shape<'i>) (path: Path) (roots: 'i list) : 'i option =
        let rec go (p: Path) (items: 'i list) : 'i option =
            match p with
            | [] -> None
            | [ i ] -> nth i items
            | i :: rest -> (match nth i items with Some x -> go rest (shape.Kids x) | None -> None)
        go path roots

    let private insertInto (shape: Shape<'i>) (at: Path) (item: 'i) (roots: 'i list) : 'i list option =
        let put (items: 'i list) (i: int) = if i < 0 || i > List.length items then None else Some (insertIndex i item items, None)
        within shape at put roots |> Option.map fst

    let private takeOut (shape: Shape<'i>) (at: Path) (roots: 'i list) : ('i list * 'i) option =
        let take (items: 'i list) (i: int) = (match nth i items with Some x -> Some (removeAt i items, Some x) | None -> None)
        match within shape at take roots with
        | Some (rest, Some item) -> Some (rest, item)
        | _ -> None

    /// Do an operation.  An error says which part found no place; the document is not changed then.
    let rec apply (shape: Shape<'i>) (op: Op<'i>) (roots: 'i list) : Result<'i list, string> =
        match op with
        | Insert (at, item) ->
            (match insertInto shape at item roots with Some r -> Ok r | None -> Error "insert: there is no such place")
        | Remove (at, _) ->
            (match takeOut shape at roots with Some (r, _) -> Ok r | None -> Error "remove: there is no such item")
        | Replace (at, _, after) ->
            let put (items: 'i list) (i: int) = (match nth i items with Some _ -> Some (replaceAt i after items, None) | None -> None)
            (match within shape at put roots with Some (r, _) -> Ok r | None -> Error "replace: there is no such item")
        | Move (source, target) ->
            (match takeOut shape source roots with
             | None -> Error "move: there is no such item"
             | Some (rest, item) -> apply shape (Insert (target, item)) rest)
        | Many ops -> applyAll shape ops roots

    and private applyAll (shape: Shape<'i>) (ops: Op<'i> list) (roots: 'i list) : Result<'i list, string> =
        match ops with
        | [] -> Ok roots
        | op :: rest ->
            (match apply shape op roots with
             | Ok r -> applyAll shape rest r
             | Error e -> Error e)

    /// The operation that undoes `op`.  `invert (invert op) = op`, and applying one after the other gives the document back.
    let rec invert (op: Op<'i>) : Op<'i> =
        match op with
        | Insert (at, item) -> Remove (at, item)
        | Remove (at, item) -> Insert (at, item)
        | Move (source, target) -> Move (target, source)
        | Replace (at, before, after) -> Replace (at, after, before)
        | Many ops -> Many (List.rev (List.map invert ops))

    /// The operation that removes the item at `path` (read from the document, so that it can be undone).
    let remove (shape: Shape<'i>) (path: Path) (roots: 'i list) : Op<'i> option =
        get shape path roots |> Option.map (fun item -> Remove (path, item))

    /// The operation that changes the item at `path` with `f`.
    let change (shape: Shape<'i>) (path: Path) (f: 'i -> 'i) (roots: 'i list) : Op<'i> option =
        get shape path roots |> Option.map (fun item -> Replace (path, item, f item))

    let private insertPath (shape: Shape<'i>) (place: Place) (roots: 'i list) : Path option =
        match place with
        | Before p -> if List.isEmpty p then None else Some p
        | After p ->
            (match List.rev p with
             | last :: up -> Some (List.rev ((last + 1) :: up))
             | [] -> None)
        | Into p ->
            (match get shape p roots with
             | Some parent -> Some (p @ [ List.length (shape.Kids parent) ])
             | None -> None)

    /// The insert that puts a new item at `place`, read in the document as it is now.  None when there is no such place: a view shows no drop mark for it.
    let insertAt (shape: Shape<'i>) (place: Place) (item: 'i) (roots: 'i list) : Op<'i> option =
        insertPath shape place roots
        |> Option.map (fun at -> Insert (at, item))
        |> Option.filter (fun op -> match apply shape op roots with Ok _ -> true | Error _ -> false)

    /// A position given for the document as it is, as it reads once the moved item is out: the indices after it shift down by one, and a place
    /// inside the item itself is none at all.
    let private withoutSource (source: Path) (target: Path) : Path option =
        let d = List.length source - 1
        if d < 0 || List.length target <= d then Some target
        elif List.truncate d source <> List.truncate d target then Some target
        else
            let si = List.item d source
            let ti = List.item d target
            if ti > si then Some (List.truncate d target @ [ ti - 1 ] @ List.skip (d + 1) target)
            elif ti = si && List.length target > d + 1 then None
            else Some target

    /// The move that a drop asks for: take the item at `source` and put it at `place`, both read in the document as it is now.  None when the drop
    /// is not possible (into the item itself, no such place) or changes nothing (right where it already is): a view shows no drop mark then.
    let drop (shape: Shape<'i>) (source: Path) (place: Place) (roots: 'i list) : Op<'i> option =
        match get shape source roots, insertPath shape place roots with
        | Some _, Some target ->
            (match withoutSource source target with
             | Some t -> if t = source then None else Some (Move (source, t))
             | None -> None)
        | _ -> None

/// A history: what was done, and what was undone.  Typing is many small edits of one thing, which undo takes back in one step: an edit with the
/// same key as the one before it (and the same place) is folded into it.
type Entry<'item> = { Op: Op<'item>; Key: string }

type History<'item> =
    { Past: Entry<'item> list
      Future: Entry<'item> list
      /// the most that is kept
      Limit: int
      /// the key of the edit that is still being typed ("" when none): undo and redo end it
      Open: string }

module History =

    let create (limit: int) : History<'i> = { Past = []; Future = []; Limit = limit; Open = "" }
    let canUndo (h: History<'i>) = not (List.isEmpty h.Past)
    let canRedo (h: History<'i>) = not (List.isEmpty h.Future)

    /// Note an operation that was done.  A new edit ends what was undone (the future).  With a key, a `Replace` of the same place that follows
    /// one with that key is folded into it.
    let record (key: string) (op: Op<'i>) (h: History<'i>) : History<'i> =
        let folded =
            match op, h.Past with
            | Replace (p2, _, after), first :: older when key <> "" && key = h.Open ->
                (match first.Op with
                 | Replace (p1, before, _) when p1 = p2 -> Some ({ Op = Replace (p1, before, after); Key = key } :: older)
                 | _ -> None)
            | _ -> None
        let past =
            match folded with
            | Some p -> p
            | None -> { Op = op; Key = key } :: List.truncate (h.Limit - 1) h.Past
        { h with Past = past; Future = []; Open = key }

    /// Do an operation and note it.
    let commit (shape: Shape<'i>) (key: string) (op: Op<'i>) (roots: 'i list) (h: History<'i>) : Result<'i list * History<'i>, string> =
        match Ops.apply shape op roots with
        | Ok r -> Ok (r, record key op h)
        | Error e -> Error e

    let undo (shape: Shape<'i>) (roots: 'i list) (h: History<'i>) : ('i list * History<'i>) option =
        match h.Past with
        | [] -> None
        | e :: rest ->
            (match Ops.apply shape (Ops.invert e.Op) roots with
             | Ok r -> Some (r, { h with Past = rest; Future = e :: h.Future; Open = "" })
             | Error _ -> None)

    let redo (shape: Shape<'i>) (roots: 'i list) (h: History<'i>) : ('i list * History<'i>) option =
        match h.Future with
        | [] -> None
        | e :: rest ->
            (match Ops.apply shape e.Op roots with
             | Ok r -> Some (r, { h with Past = e :: h.Past; Future = rest; Open = "" })
             | Error _ -> None)

/// An edit in place: the item as it was, and as it is being typed.  Escape drops it; committing gives one `Replace` (none when nothing changed).
type Session<'item> = { At: Path; Original: 'item; Draft: 'item }

module Inline =

    let start (at: Path) (item: 'i) : Session<'i> = { At = at; Original = item; Draft = item }
    let change (draft: 'i) (s: Session<'i>) : Session<'i> = { s with Draft = draft }
    let commit (s: Session<'i>) : Op<'i> option =
        if s.Draft = s.Original then None else Some (Replace (s.At, s.Original, s.Draft))
