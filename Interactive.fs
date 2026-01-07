module Flappy.Interactive

open System

let private clearLine () =
    let currentLine = Console.CursorTop
    Console.SetCursorPosition(0, currentLine)
    Console.Write(new String(' ', Console.WindowWidth))
    Console.SetCursorPosition(0, currentLine)

let select (prompt: string) (options: string list) (defaultIndex: int) : string option =
    let mutable index = defaultIndex
    let mutable done' = false
    let mutable cancelled = false

    // Hide cursor
    let originalCursorVisible = Console.CursorVisible
    Console.CursorVisible <- false

    Console.WriteLine $"{prompt} (Use arrow keys to move, Enter to select, Esc to cancel)"

    // Ensure we have enough space at the bottom
    let neededLines = options.Length
    let bottom = Console.CursorTop + neededLines

    if bottom >= Console.BufferHeight then
        let overflow = bottom - Console.WindowHeight

        if overflow > 0 then
            for _ in 1 .. overflow + 1 do
                Console.WriteLine()

    let startTop = Console.CursorTop

    for _ in options do
        Console.WriteLine(new String(' ', Console.WindowWidth - 1))

    let draw () =
        Console.SetCursorPosition(0, startTop)

        options
        |> List.iteri (fun i opt ->
            Console.Write(new String(' ', Console.WindowWidth - 1))
            Console.SetCursorPosition(0, startTop + i)

            if i = index then
                Console.ForegroundColor <- ConsoleColor.Cyan
                Console.WriteLine $"> {opt}"
                Console.ResetColor()
            else
                Console.WriteLine $"  {opt}")

    try
        while not done' do
            draw ()
            let key = Console.ReadKey true

            match key.Key with
            | ConsoleKey.UpArrow -> index <- max 0 (index - 1)
            | ConsoleKey.DownArrow -> index <- min (options.Length - 1) (index + 1)
            | ConsoleKey.Enter -> done' <- true
            | ConsoleKey.Escape ->
                cancelled <- true
                done' <- true
            | _ -> ()

        // Clear options space
        Console.SetCursorPosition(0, startTop)

        for _ in options do
            clearLine ()
            Console.WriteLine("")

        Console.SetCursorPosition(0, startTop)

        if cancelled then None else Some options.[index]
    finally
        Console.CursorVisible <- originalCursorVisible
