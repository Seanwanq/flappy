namespace Flappy.Core

open System

module Log = 
    let info (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Green
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)

    let warn (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Yellow
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)

    let error (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Red
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)
