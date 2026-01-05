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
    
    // Ensure we have enough space at the bottom
    let neededLines = options.Length
    let bottom = Console.CursorTop + neededLines
    if bottom >= Console.BufferHeight then
        // If we are close to the buffer limit, this is tricky.
        // But usually BufferHeight is large. If it's small (WindowHeight), we might scroll.
        // Let's just print newlines to force scroll if we are near WindowHeight
        let overflow = bottom - Console.WindowHeight
        if overflow > 0 then
             for _ in 1 .. overflow + 1 do Console.WriteLine()
    
    // Now capture the safe starting position
    let startTop = Console.CursorTop
    
    // Reserve the space explicitly by printing blank lines
    // This ensures that even if we are at the bottom, we "own" these lines now.
    for _ in options do Console.WriteLine(new String(' ', Console.WindowWidth - 1))
    
    // Now we can safely jump back to startTop
    
    let draw () =
        Console.SetCursorPosition(0, startTop)
        options |> List.iteri (fun i opt ->
            // Clear line carefully
            Console.Write(new String(' ', Console.WindowWidth - 1))
            Console.SetCursorPosition(0, startTop + i)
            
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
