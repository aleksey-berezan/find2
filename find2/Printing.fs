[<AutoOpen>]
module Printing

open System

type ColorBuilder(color:ConsoleColor) =
    member this.Bind(x, f) = f(x)
    member this.Delay(f) =          
        let oldColor = Console.ForegroundColor
        System.Console.ForegroundColor <- color
        try            
            f() |> ignore
        finally
            Console.ForegroundColor <- oldColor 
    member this.Return(x) = Some x

let error = ColorBuilder(ConsoleColor.Red)
let warning = ColorBuilder(ConsoleColor.Yellow)
let emphasize = ColorBuilder(ConsoleColor.White)