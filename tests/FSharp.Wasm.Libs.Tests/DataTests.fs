/// The tests of FSharp.Wasm.Data: JSON and numbers.  Written with Wasmecto, run under .NET (`dotnet run`) and as WebAssembly GC
/// (`bash build/test.sh wasm`): the library is meant for modules, so that is the run that counts.
module FSharp.Wasm.Libs.DataTests

open Wasmecto
open FSharp.Wasm.Data

let private has (haystack: string) (needle: string) = Expect.stringContains haystack needle ("contains " + needle)
let private isError (r: Result<Json, string>) = match r with Error _ -> true | Ok _ -> false

let numbers =
    testList "Numbers" [
        testCase "integers: sign, digits, range" <| fun () ->
            Expect.equal (Numbers.parseInt "42") (Some 42) "plain"
            Expect.equal (Numbers.parseInt "-7") (Some -7) "negative"
            Expect.equal (Numbers.parseInt "2147483647") (Some 2147483647) "max"
            Expect.equal (Numbers.parseInt "2147483648") None "too big"
            Expect.equal (Numbers.parseInt "-2147483648") (Some -2147483648) "min"
            Expect.equal (Numbers.parseInt "") None "empty"
            Expect.equal (Numbers.parseInt "-") None "sign only"
            Expect.equal (Numbers.parseInt "4x") None "garbage"
            Expect.equal (Numbers.parseUInt32 "4294967295") (Some 4294967295u) "uint max"
            Expect.equal (Numbers.parseUInt32 "4294967296") None "uint overflow"
        testCase "floats follow the JSON grammar" <| fun () ->
            Expect.equal (Numbers.parseFloat "1500") (Some 1500.0) "integer"
            Expect.equal (Numbers.parseFloat "1.5e3") (Some 1500.0) "exponent"
            Expect.equal (Numbers.parseFloat "-0.25") (Some -0.25) "negative fraction"
            Expect.equal (Numbers.parseFloat "123456789012345") (Some 123456789012345.0) "15 digits are exact"
            Expect.equal (Numbers.parseFloat "0.1") (Some 0.1) "a tenth"
            Expect.equal (Numbers.parseFloat "2.5E-1") (Some 0.25) "negative exponent"
            for bad in [ ""; "-"; "."; "1."; ".5"; "1e"; "1e+"; "abc"; "1.2.3" ] do
                Expect.equal (Numbers.parseFloat bad) None ("not a number: " + bad)
        testCase "finite" <| fun () ->
            Expect.isTrue (Numbers.isFinite 1.0) "one"
            Expect.isFalse (Numbers.isFinite (1.0 / 0.0)) "infinity"
            Expect.isFalse (Numbers.isFinite (0.0 / 0.0)) "NaN"
    ]

let json =
    let value =
        JObj [ "text", JStr "line\nbreak \"quoted\" \\ tab\t ünï ✓ 🚀 \u0001"
               "n", JNum 42.0
               "pi", JNum 3.25
               "neg", JNum -7.0
               "flags", JArr [ JBool true; JBool false; JNull ]
               "nested", JObj [ "empty", JArr []; "o", JObj [] ] ]
    testList "Json" [
        testCase "text round-trips, compact and pretty" <| fun () ->
            Expect.equal (Json.parse (Json.stringify value)) (Ok value) "compact"
            Expect.equal (Json.parse (Json.pretty value)) (Ok value) "pretty"
        testCase "the writer: escapes, integers without a fraction, no NaN" <| fun () ->
            has (Json.stringify (JStr "a\"b")) "a\\\"b"
            has (Json.stringify (JStr "\u0001")) "\\u0001"
            Expect.equal (Json.stringify (JNum 42.0)) "42" "whole numbers"
            Expect.equal (Json.stringify (JNum(0.0 / 0.0))) "null" "NaN has no JSON form"
            Expect.equal (Json.stringify (JObj [ "a", JArr [ JNum 1.0; JNull ] ])) "{\"a\":[1,null]}" "compact"
            Expect.equal (Json.pretty (JObj [ "a", JArr [ JNum 1.0 ]; "b", JObj [] ])) "{\n  \"a\": [\n    1\n  ],\n  \"b\": {}\n}" "pretty"
        testCase "the parser reads standard JSON, with escapes and surrogate pairs" <| fun () ->
            Expect.equal (Json.tryParse "\"\\u00e9\"") (Some(JStr "é")) "\\u escape"
            Expect.equal (Json.tryParse "\"\\ud83d\\ude80\"") (Some(JStr "🚀")) "a surrogate pair"
            Expect.equal (Json.tryParse " { \"k\" : [ 1 , 2 ] } ") (Some(JObj [ "k", JArr [ JNum 1.0; JNum 2.0 ] ])) "whitespace"
            Expect.equal (Json.tryParse "1.5e3") (Some(JNum 1500.0)) "exponent"
            Expect.equal (Json.tryParse "[true,false,null]") (Some(JArr [ JBool true; JBool false; JNull ])) "literals"
        testCase "what is not JSON is refused, and never raises" <| fun () ->
            for bad in [ ""; "{"; "}"; "[1,"; "{\"a\"}"; "{\"a\":}"; "{a:1}"; "[1 2]"; "\"unterminated"; "\"bad \\q escape\""; "tru"; "nul"; "01x"; "{\"a\":1} extra"; "\"\\u12\""; "\"tab\there\"" ] do
                Expect.isTrue (isError (Json.parse bad)) ("refused: " + bad)
                Expect.equal (Json.tryParse bad) None ("no value: " + bad)
        testCase "the error names the position of the first problem" <| fun () ->
            Expect.equal (Json.parse "[1,]") (Error "unexpected character at position 3") "after the comma"
            Expect.equal (Json.parse "") (Error "unexpected end at position 0") "empty"
            has (match Json.parse "{\"a\" 1}" with Error m -> m | Ok _ -> "") "expected ':'"
        testCase "nesting is bounded, so a hostile document cannot overflow the stack" <| fun () ->
            Expect.equal (Json.tryParse (String.replicate 1000 "[" + String.replicate 1000 "]")) None "1000 levels"
            Expect.isSome (Json.tryParse (String.replicate 60 "[" + String.replicate 60 "]")) "60 levels are fine"
        testCase "field and the accessors" <| fun () ->
            let v = (Json.tryParse "{\"text\":\"hi\",\"n\":3,\"ok\":true,\"xs\":[1,2]}").Value
            Expect.equal (Json.asString (Json.field "text" v)) (Some "hi") "string"
            Expect.equal (Json.asNumber (Json.field "n" v)) (Some 3.0) "number"
            Expect.equal (Json.asBool (Json.field "ok" v)) (Some true) "bool"
            Expect.equal (Json.asString (Json.field "missing" v)) None "missing"
            Expect.equal (Json.asNumber (Json.field "text" v)) None "wrong type"
        testCase "accessors with a fallback: a document with a missing field still loads" <| fun () ->
            let v = (Json.tryParse "{\"s\":\"v\",\"n\":3,\"b\":true,\"xs\":[1,2]}").Value
            Expect.equal (Json.stringOr "s" "d" v) "v" "present"
            Expect.equal (Json.stringOr "x" "d" v) "d" "missing"
            Expect.equal (Json.intOr "s" 7 v) 7 "wrong type falls back"
            Expect.equal (Json.intOr "n" 7 v) 3 "int"
            Expect.equal (Json.numberOr "n" 0.0 v) 3.0 "number"
            Expect.equal (Json.boolOr "b" false v) true "bool"
            Expect.equal (Json.listOf "xs" v) [ JNum 1.0; JNum 2.0 ] "list"
            Expect.equal (Json.listOf "nope" v) [] "no list"
    ]

let suites : Test list = [ numbers; json ]
