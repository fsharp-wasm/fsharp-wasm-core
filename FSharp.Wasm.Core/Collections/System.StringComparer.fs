// StringComparer: the comparers that `Dictionary(comparer)` and `HashSet(comparer)` take for strings.  Its own file: a library file with two namespaces
// is compiled with its members named by namespace (`Collections_Generic_Dictionary$2__…`), which the collections' own call sites do not expect.
namespace System

open System.Collections.Generic

/// `StringComparer.Ordinal` and `StringComparer.OrdinalIgnoreCase`; the culture-sensitive ones of the BCL are the ordinal ones here.
/// Ignoring case folds with `Char.ToUpper`, which knows the ASCII letters only (a known gap, like `Char.IsLetter`).
type StringComparer(ignoreCase: bool) =
    let fold (c: char) : int = if ignoreCase then int (System.Char.ToUpper c) else int c

    member _.Compare(x: string, y: string) : int =
        let n = min x.Length y.Length
        let mutable i = 0
        let mutable d = 0
        while d = 0 && i < n do
            d <- fold x.[i] - fold y.[i]
            i <- i + 1
        if d <> 0 then d else x.Length - y.Length

    member this.SameText(x: string, y: string) : bool = x.Length = y.Length && this.Compare(x, y) = 0

    member _.HashOf(s: string) : int =
        let mutable h = 5381
        for i in 0 .. s.Length - 1 do h <- (h * 33) ^^^ fold s.[i]
        h

    static member Ordinal : StringComparer = StringComparer(false)
    static member OrdinalIgnoreCase : StringComparer = StringComparer(true)
    static member InvariantCulture : StringComparer = StringComparer(false)
    static member InvariantCultureIgnoreCase : StringComparer = StringComparer(true)

    interface IEqualityComparer<string> with
        member this.Equals(x: string, y: string) : bool = this.SameText(x, y)
        member this.GetHashCode(s: string) : int = this.HashOf s
    interface IComparer<string> with
        member this.Compare(x: string, y: string) : int = this.Compare(x, y)
