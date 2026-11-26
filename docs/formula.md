# Formulas

`FSharp.Wasm.Formula` is what a cell of a spreadsheet needs: the text a person types, what it means, what it is worth, and what has to be calculated again when something changes. Nothing to depend on but the
base library and the number parsing of `FSharp.Wasm.Data`; it runs under .NET and as WebAssembly GC, and is tested both ways (`tests/FSharp.Wasm.Libs.Tests/FormulaTests.fs`).

```fsharp
let book = Book()
book.Set(0, 0, "2")                    // A1
book.Set(0, 1, "3")                    // A2
book.Set(0, 2, "=A1*A2")               // A3
let changes = book.Set(0, 0, "5")      // A1, then A3: [| { Cell = A1; Before = 2; After = 5 }; { Cell = A3; Before = 6; After = 15 } |]
```

## The language

Numbers (`1.5`, `.5`, `2e3`, `50%`), text in double quotes (a quote inside is doubled), `TRUE` and `FALSE`, cells (`A1`, `$A$1`, `B$2`), ranges (`A1:B5`), names the host knows (`Actual`) and calls (`SUM(A1:A3, 4)`;
names in any case; `,` or `;` between arguments). Operators, from the loosest: comparisons (`= <> < <= > >=`), `&`, `+ -`, `* /`, `^`; a prefix minus and a postfix `%` bind tighter. As in a spreadsheet `-2^2`
is 4 and `^` goes from the left. An error says where: `1+` fails at the end, `SUM(1 2)` at the second argument (`ParseError.Position`).

Functions: `SUM AVERAGE MIN MAX COUNT PRODUCT ABS INT SQRT ROUND MOD POWER IF IFERROR AND OR NOT CONCAT`. `IF` evaluates the branch it takes and not the other (`IF(B1=0,0,A1/B1)` is not a division by zero);
`ROUND` goes half away from zero; text and blanks in a range are left out of the sums. The host adds functions through `Env.Custom` and names through `Env.Name` (which cell holds the formula is known, so
`Actual` can mean the actual of this cell's row and column).

Errors are values, the first one found is the answer, and they travel through the cells that read them: `#DIV/0!`, `#NAME?`, `#VALUE!`, `#REF!`, `#NUM!`, `#CYCLE!`, and `#ERROR!` for text after `=` that does
not parse (the text is kept, to be corrected).

## The book

`Set(col, row, text)` takes what was typed: a formula (starts with `=`), a number, text, or nothing (clears the cell). It parses, finds what the cell reads (single cells, and ranges as boxes), and calculates
the cell and then everything that depends on it, directly or through others, **in an order in which a cell comes after the cells it reads and once each**. The result is the cells whose value changed, in that
order, with what they were: what a view repaints, and what another place needs to be told (the patch of the edit). A value that comes out the same is not a change.

Cells that read each other are `#CYCLE!`, every one of the cycle and not an endless loop, and the cycle is gone when it is broken. The walk keeps a stack of its own, so a column of running totals as long as
the sheet is a chain and not a call stack. The sheet is 16 384 columns by 16 384 rows (a cell is one number, `Row * 16384 + Col`).

What lies outside the book (the names and functions of the host) is told with `Invalidate(cells)` or `Recalculate()`.

## Not yet

- A range that is read by many formulas is checked against the cells that changed one by one (formulas with ranges are few); a reverse index of ranges would make a large sheet of them faster.
- No references to another sheet, no array formulas, no dates, and the number formatting is the host's.
