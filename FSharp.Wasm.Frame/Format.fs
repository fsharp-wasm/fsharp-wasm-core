namespace FSharp.Wasm.Frame

open System.Text

module Format =

    /// An amount as a ledger shows it: thousands separated by a comma, two decimals, a minus for a negative that is not zero when rounded; a dash for what is not a number (a
    /// division by zero).  No culture is asked: the same text in every host.
    let amount (x: float) : string =
        if x <> x then "–"
        else
            let cents = int64 (floor (abs x * 100.0 + 0.5))
            let whole = string (cents / 100L)
            let frac = int (cents % 100L)
            let sb = StringBuilder()
            if x < 0.0 && cents > 0L then sb.Append '-' |> ignore
            for i in 0 .. whole.Length - 1 do
                if i > 0 && (whole.Length - i) % 3 = 0 then sb.Append ',' |> ignore
                sb.Append whole.[i] |> ignore
            sb.Append('.').Append(char (48 + frac / 10)).Append(char (48 + frac % 10)) |> ignore
            sb.ToString()
