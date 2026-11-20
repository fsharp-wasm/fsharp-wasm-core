namespace FSharp.Wasm.Data

/// Number parsing and checks written out (no Int32.TryParse, Double.TryParse, Double.IsNaN): what a request contains — an id, a
/// version, a JSON number — and nothing more.
module Numbers =
    let private isDigit (c: char) = c >= '0' && c <= '9'

    /// A finite number: NaN and the infinities are not (`n - n` is 0 only for those that are).
    let isFinite (n: float) : bool = n - n = 0.0

    /// An optional minus and at least one digit, nothing else; None when the value does not fit an int.
    let parseInt (s: string) : int option =
        let negative = s.Length > 0 && s.[0] = '-'
        let start = if negative then 1 else 0
        if s.Length = start then None
        else
            let mutable value = 0L
            let mutable ok = true
            for i in start .. s.Length - 1 do
                if isDigit s.[i] then
                    if value <= 2147483648L then value <- value * 10L + int64 (int s.[i] - int '0')
                else ok <- false
            if not ok then None
            elif negative && value <= 2147483648L then Some(int (-value))
            elif not negative && value <= 2147483647L then Some(int value)
            else None

    /// Digits only, up to 4294967295.
    let parseUInt32 (s: string) : uint32 option =
        if s.Length = 0 || s.Length > 10 then None
        else
            let mutable value = 0UL
            let mutable ok = true
            for c in s do
                if isDigit c then value <- value * 10UL + uint64 (int c - int '0') else ok <- false
            if ok && value <= 4294967295UL then Some(uint32 value) else None

    let private pow10 (n: int) : float =
        let mutable r = 1.0
        for _ in 1 .. n do r <- r * 10.0
        r

    /// JSON number grammar:  -? digits (. digits)? ((e|E) (+|-)? digits)?   Exact for integers and for numbers of up to 15
    /// significant digits; longer ones are close, not correctly rounded.
    let parseFloat (s: string) : float option =
        let n = s.Length
        let mutable i = 0
        let negative = n > 0 && s.[0] = '-'
        if negative then i <- 1
        let intStart = i
        let mutable mantissa = 0.0
        while i < n && isDigit s.[i] do
            mantissa <- mantissa * 10.0 + float (int s.[i] - int '0')
            i <- i + 1
        let hasInt = i > intStart
        let mutable fractionDigits = 0
        let mutable hasFraction = true
        if i < n && s.[i] = '.' then
            i <- i + 1
            let fractionStart = i
            while i < n && isDigit s.[i] do
                mantissa <- mantissa * 10.0 + float (int s.[i] - int '0')
                fractionDigits <- fractionDigits + 1
                i <- i + 1
            hasFraction <- i > fractionStart
        let mutable exponent = 0
        let mutable hasExponent = true
        if i < n && (s.[i] = 'e' || s.[i] = 'E') then
            i <- i + 1
            let expNegative = i < n && s.[i] = '-'
            if i < n && (s.[i] = '-' || s.[i] = '+') then i <- i + 1
            let expStart = i
            while i < n && isDigit s.[i] && exponent < 1000 do
                exponent <- exponent * 10 + (int s.[i] - int '0')
                i <- i + 1
            hasExponent <- i > expStart
            if expNegative then exponent <- -exponent
        if not hasInt || not hasFraction || not hasExponent || i <> n then None
        else
            let scale = exponent - fractionDigits
            let value = if scale >= 0 then mantissa * pow10 scale else mantissa / pow10 (-scale)
            Some(if negative then -value else value)
