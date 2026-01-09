module Flappy.Interactive

open Spectre.Console

let select (prompt: string) (options: string list) (defaultIndex: int) : string option =
    try
        let promptObj = SelectionPrompt<string>()
                            .Title(prompt)
                            .PageSize(10)
                            .AddChoices(options)
        
        // Invoke the prompt
        let selection = AnsiConsole.Prompt(promptObj)
        Some selection
    with 
    | _ -> None
