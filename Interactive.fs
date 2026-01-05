module Flappy.Interactive

open System

let private clearLine () =
    let currentLine = Console.CursorTop
    Console.SetCursorPosition(0, currentLine)
    Console.Write(new String(' ', Console.WindowWidth))
    Console.SetCursorPosition(0, currentLine)

let select (prompt: string) (options: string list) (defaultIndex: int) : string =
    let mutable index = defaultIndex
    let mutable done' = false
    
    // Hide cursor
    let originalCursorVisible = Console.CursorVisible
    Console.CursorVisible <- false
    
    Console.WriteLine($"{prompt} (Use arrow keys to move, Enter to select)")
    let startTop = Console.CursorTop

    let draw () =
        Console.SetCursorPosition(0, startTop)
        options |> List.iteri (fun i opt ->
            if i = index then
                Console.ForegroundColor <- ConsoleColor.Cyan
                Console.WriteLine($"> {opt}")
                Console.ResetColor()
            else
                Console.WriteLine($"  {opt}")
        )

    try
        while not done' do
            draw ()
            let key = Console.ReadKey(true)
            match key.Key with
            | ConsoleKey.UpArrow ->
                index <- max 0 (index - 1)
            | ConsoleKey.DownArrow ->
                index <- min (options.Length - 1) (index + 1)
            | ConsoleKey.Enter ->
                done' <- true
            | ConsoleKey.Escape ->
                // Cancel operation? For now just select current or throw?
                // Let's assume selection is mandatory or default.
                done' <- true
            | _ -> ()
            
        // Final draw to show selection
        Console.SetCursorPosition(0, startTop)
        // Clear options space
        for _ in options do
            clearLine()
            Console.WriteLine("")
        
        Console.SetCursorPosition(0, startTop)
        // Return selected
        options.[index]
    finally
        Console.CursorVisible <- originalCursorVisible
