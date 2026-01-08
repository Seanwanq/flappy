module Flappy.Interactive

open System

let private clearLine () =
    let currentLine = Console.CursorTop
    if currentLine >= 0 && currentLine < Console.BufferHeight then
        Console.SetCursorPosition(0, currentLine)
        Console.Write(new String(' ', Console.WindowWidth))
        Console.SetCursorPosition(0, currentLine)

let select (prompt: string) (options: string list) (defaultIndex: int) : string option =
    let mutable index = defaultIndex
    let mutable done' = false
    let mutable cancelled = false
    
    let originalCursorVisible = Console.CursorVisible
    Console.CursorVisible <- false
    
    Console.WriteLine($"{prompt} (Use arrow keys to move, Enter to select, Esc to cancel)")
    
    let neededLines = options.Length
    let mutable startTop = Console.CursorTop
    
    if startTop + neededLines >= Console.BufferHeight then
        let overflow = (startTop + neededLines) - Console.BufferHeight + 1
        for _ in 1 .. overflow do Console.WriteLine()
        startTop <- max 0 (Console.CursorTop - neededLines)
    
    let draw () =
        for i in 0 .. options.Length - 1 do
            let targetTop = startTop + i
            if targetTop >= 0 && targetTop < Console.BufferHeight then
                Console.SetCursorPosition(0, targetTop)
                let opt = options.[i]
                if i = index then
                    Console.ForegroundColor <- ConsoleColor.Cyan
                    Console.Write($"> {opt}".PadRight(Console.WindowWidth - 1))
                    Console.ResetColor()
                else
                    Console.Write($"  {opt}".PadRight(Console.WindowWidth - 1))

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
                cancelled <- true
                done' <- true
            | _ -> ()
            
        for i in 0 .. options.Length - 1 do
            let targetTop = startTop + i
            if targetTop >= 0 && targetTop < Console.BufferHeight then
                Console.SetCursorPosition(0, targetTop)
                clearLine()
        
        if startTop >= 0 && startTop < Console.BufferHeight then
            Console.SetCursorPosition(0, startTop)
        
        if cancelled then None else Some options.[index]
    finally
        Console.CursorVisible <- originalCursorVisible