namespace FSharp.Wasm.Frame

open System.Text

module Format =

    /// The digits of a whole number with a comma between the thousands.
    let private grouped (sb: StringBuilder) (digits: string) : unit =
        for i in 0 .. digits.Length - 1 do
            if i > 0 && (digits.Length - i) % 3 = 0 then sb.Append ',' |> ignore
            sb.Append digits.[i] |> ignore

    /// An amount as a ledger shows it: thousands separated by a comma, two decimals, a minus for a negative that is not zero when rounded; a dash for what is not a number (a
    /// division by zero).  No culture is asked: the same text in every host.
    let amount (x: float) : string =
        if x <> x then "–"
        else
            let cents = int64 (floor (abs x * 100.0 + 0.5))
            let frac = int (cents % 100L)
            let sb = StringBuilder()
            if x < 0.0 && cents > 0L then sb.Append '-' |> ignore
            grouped sb (string (cents / 100L))
            sb.Append('.').Append(char (48 + frac / 10)).Append(char (48 + frac % 10)) |> ignore
            sb.ToString()

    /// A count: the digits with a comma between the thousands (`1,000,000`).
    let count (n: int64) : string =
        let sb = StringBuilder()
        if n < 0L then sb.Append '-' |> ignore
        grouped sb (string (abs n))
        sb.ToString()

    /// Milliseconds with one decimal, rounded (`3.4`), for the time a step took.
    let millis (ms: float) : string =
        let tenths = int64 (floor (abs ms * 10.0 + 0.5))
        (if ms < 0.0 && tenths > 0L then "-" else "") + string (tenths / 10L) + "." + string (tenths % 10L)
