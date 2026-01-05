module Flappy.Program

open System
open Flappy.Scaffolding
open Flappy.Builder
open Flappy.GlobalConfig
open Flappy.Toolchain

let getOrSetupCompiler () =
    match loadConfig() with
    | Some config -> config.DefaultCompiler
    | None ->
        printfn "Welcome to Flappy! First-time setup..."
        printfn "Scanning for C++ toolchains..."
        let toolchains = getAvailableToolchains()
        
        if toolchains.IsEmpty then
            printfn "No standard C++ toolchains found (MSVC, g++, clang++)."
            printfn "Recommended: Install Visual Studio with C++ workload or MinGW."
            printf "Enter command for your compiler (e.g. g++): "
            let input = Console.ReadLine()
            let compiler = if String.IsNullOrWhiteSpace(input) then "g++" else input.Trim()
            saveConfig { DefaultCompiler = compiler }
            compiler
        else
            printfn "Found the following toolchains:"
            toolchains |> List.iteri (fun i t -> printfn "[%d] %s (%s)" (i + 1) t.Name t.Command)
            
            printf "Select a default compiler [1-%d]: " toolchains.Length
            let input = Console.ReadLine()
            match Int32.TryParse(input) with
            | true, idx when idx >= 1 && idx <= toolchains.Length ->
                let selected = toolchains.[idx - 1].Command
                saveConfig { DefaultCompiler = selected }
                printfn "Selected %s as default." selected
                selected
            | _ ->
                let defaultCompiler = toolchains.[0].Command
                printfn "Invalid selection. Defaulting to %s." defaultCompiler
                saveConfig { DefaultCompiler = defaultCompiler }
                defaultCompiler

[<EntryPoint>]
let main args =
    match args with
    | [| "init"; name |] -> 
        let compiler = getOrSetupCompiler()
        initProject name compiler
        0
    | [| "build" |] ->
        match build() with
        | Ok () -> 
            printfn "Build successful."
            0
        | Error e -> 
            eprintfn "Build failed: %s" e
            1
    | [| "run" |] ->
        match run() with
        | Ok () -> 0
        | Error e ->
            eprintfn "Run failed: %s" e
            1
    | _ ->
        printfn "Usage: flappy <command> [options]"
        printfn "Commands:"
        printfn "  init <name>   Create a new project"
        printfn "  build         Build the project"
        printfn "  run           Build and run the project"
        1
