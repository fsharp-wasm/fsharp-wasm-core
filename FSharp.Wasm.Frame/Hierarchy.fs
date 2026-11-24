namespace FSharp.Wasm.Frame

open System.Collections.Generic

/// One node of a hierarchy.  Every node covers a contiguous run of leaves, [Lo, Hi): that is what makes the total of a group one rectangle of a matrix.
type Node =
    { Id: int
      Label: string
      /// 0 for the first level below the root
      Level: int
      /// -1 for the root
      Parent: int
      Lo: int
      Hi: int
      /// in order; none for a leaf
      Children: int[] }

/// A tree over an ordered set of leaves (the accounts of a chart, the months of two years).  Node 0 is the root, which is not shown.
type Hierarchy =
    { Nodes: Node[]
      Leaves: int
      /// the node of every leaf, by leaf number
      LeafNodes: int[]
      Index: Dictionary<string, int> }

module Hierarchy =

    let private joined (path: string[]) : string = String.concat "\u001f" path

    /// The tree of the paths, one per leaf (`[| "Revenue"; "Sales"; "4000 Sales EU" |]`), in the order they are given: that is the order of the leaves.  Paths that share a
    /// beginning must be next to each other (sort them first), a path may not be the beginning of another, and the same path twice in a row is one leaf.
    let ofPaths (paths: string[][]) : Hierarchy =
        let labels = ResizeArray<string>()
        let levels = ResizeArray<int>()
        let parents = ResizeArray<int>()
        let los = ResizeArray<int>()
        let his = ResizeArray<int>()
        let kids = ResizeArray<ResizeArray<int>>()
        let add (label: string) (level: int) (parent: int) (lo: int) : int =
            let id = labels.Count
            labels.Add label
            levels.Add level
            parents.Add parent
            los.Add lo
            his.Add lo
            kids.Add(ResizeArray<int>())
            if parent >= 0 then kids.[parent].Add id
            id
        let root = add "" -1 -1 0
        // the open node of every level, from the top
        let stack = ResizeArray<int>()
        // a group opened twice means the paths were not grouped
        let opened = Dictionary<string, int>()
        let index = Dictionary<string, int>()
        let leafNodes = ResizeArray<int>()
        let mutable previous : string[] = [||]
        let mutable leaves = 0
        for path in paths do
            if path.Length = 0 then failwith "a path needs at least one label"
            let mutable shared = 0
            while shared < path.Length && shared < previous.Length && path.[shared] = previous.[shared] do shared <- shared + 1
            let same = shared = path.Length && shared = previous.Length
            if not same then
                if leaves > 0 && (shared = path.Length || shared = previous.Length) then failwith "a path may not be the beginning of another"
                while stack.Count > shared do
                    his.[stack.[stack.Count - 1]] <- leaves
                    stack.RemoveAt(stack.Count - 1)
                for level in shared .. path.Length - 1 do
                    let parent = if level = 0 then root else stack.[level - 1]
                    let key = string parent + "\u001f" + path.[level]
                    if opened.ContainsKey key then failwith ("paths that share a beginning must be next to each other: " + path.[level])
                    let id = add path.[level] level parent leaves
                    opened.[key] <- id
                    stack.Add id
                leafNodes.Add stack.[stack.Count - 1]
                index.[joined path] <- leaves
                leaves <- leaves + 1
                previous <- path
        while stack.Count > 0 do
            his.[stack.[stack.Count - 1]] <- leaves
            stack.RemoveAt(stack.Count - 1)
        his.[root] <- leaves
        { Nodes = Array.init labels.Count (fun i ->
              { Id = i; Label = labels.[i]; Level = levels.[i]; Parent = parents.[i]; Lo = los.[i]; Hi = his.[i]; Children = kids.[i].ToArray() })
          Leaves = leaves
          LeafNodes = leafNodes.ToArray()
          Index = index }

    /// The leaf number of a path, -1 when there is none.
    let leafOf (h: Hierarchy) (path: string[]) : int =
        match h.Index.TryGetValue(joined path) with
        | true, i -> i
        | _ -> -1

    let isLeaf (h: Hierarchy) (id: int) : bool = h.Nodes.[id].Children.Length = 0

    /// The nodes that are shown, in the order they are shown: a node, and below it its children when it is open (and so on down).  A node is its own total, whether it is
    /// open or not: opening one adds the lines of what it is made of.
    let visible (h: Hierarchy) (isOpen: int -> bool) : int[] =
        let out = ResizeArray<int>()
        let rec walk (id: int) : unit =
            out.Add id
            let children = h.Nodes.[id].Children
            if children.Length > 0 && isOpen id then
                for c in children do walk c
        for c in h.Nodes.[0].Children do walk c
        out.ToArray()

    /// The nodes that have children, as a starting point for "open everything" (or the first levels).
    let groups (h: Hierarchy) : int[] =
        h.Nodes |> Array.filter (fun n -> n.Id > 0 && n.Children.Length > 0) |> Array.map (fun n -> n.Id)
