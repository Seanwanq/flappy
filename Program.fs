module Flappy.Program

open System
open Flappy.Scaffolding
open Flappy.Builder
open Flappy.GlobalConfig
open Flappy.Toolchain
open Flappy.Interactive

let getOrSetupCompiler () =
    match loadConfig() with
    | Some config -> config.DefaultCompiler
    | None ->
        Console.WriteLine("Welcome to Flappy! First-time setup...")
        Console.WriteLine("Scanning for C++ toolchains...")
        let toolchains = getAvailableToolchains()
        
        if toolchains.IsEmpty then
            Console.WriteLine("No standard C++ toolchains found (MSVC, g++, clang++).")
            Console.WriteLine("Recommended: Install Visual Studio with C++ workload or MinGW.")
            Console.Write("Enter command for your compiler (e.g. g++): ")
            let input = Console.ReadLine()
            let compiler = if String.IsNullOrWhiteSpace(input) then "g++" else input.Trim()
            saveConfig { DefaultCompiler = compiler }
            compiler
        else
            let options = toolchains |> List.map (fun t -> $"{t.Name} ({t.Command})")
            let selection = select "Select a default compiler:" options 0
            // Extract command from selection? Or match index.
            // Simplified: Re-find based on display string
            let selected = toolchains |> List.find (fun t -> $"{t.Name} ({t.Command})" = selection)
            saveConfig { DefaultCompiler = selected.Command }
            Console.WriteLine($"Selected {selected.Command} as default.")
            selected.Command

let runInteractiveInit (nameArg: string option) =
    Console.WriteLine("Initializing new Flappy project...")
    
    // 1. Name
    let name = 
        match nameArg with
        | Some n -> n
        | None -> 
            Console.Write("Project Name: ")
            let input = Console.ReadLine()
            if String.IsNullOrWhiteSpace(input) then "untitled" else input.Trim()

    // 2. Compiler
    let toolchains = getAvailableToolchains()
    let compiler = 
        if toolchains.IsEmpty then
            Console.Write("Compiler (e.g. g++): ")
            let input = Console.ReadLine()
            if String.IsNullOrWhiteSpace(input) then "g++" else input.Trim()
        else
            let opts = toolchains |> List.map (fun t -> t.Command)
            select "Select Compiler:" opts 0

    // 3. Standard
    let standards = ["c++17"; "c++20"; "c++23"; "c++14"; "c++11"]
    let std = select "Select C++ Standard:" standards 0 // Default to c++17

    // 4. Arch
    let archs = ["x64"; "x86"; "arm64"]
    let arch = select "Select Architecture:" archs 0 // Default x64

    // 5. Type
    let types = ["exe"; "dll"]
    let type' = select "Select Project Type:" types 0 // Default exe

    { Name = name; Compiler = compiler; Standard = std; Arch = arch; Type = type' }

let parseInitArgs (args: string list) =
    let rec parse (opts: Map<string, string>) (rest: string list) =
        match rest with
        | [] -> opts
        | "-c" :: val' :: tail | "--compiler" :: val' :: tail ->
            parse (opts.Add("compiler", val')) tail
        | "-s" :: val' :: tail | "--std" :: val' :: tail ->
            parse (opts.Add("std", val')) tail
        | head :: tail ->
            Console.WriteLine($"Warning: Unknown argument '{head}' ignored.")
            parse opts tail
            
    parse Map.empty args

[<EntryPoint>]
let main args =
    let argsList = args |> Array.toList
    match argsList with
    | "init" :: tail ->
        // Check if user passed flags. If yes, use non-interactive mode (or mixed).
        // If tail is empty or just name, use interactive.
        let isFlag (s: string) = s.StartsWith("-")
        let hasFlags = tail |> List.exists isFlag
        
        if hasFlags then
            // Legacy/Flag mode
            let name, argsToParse = 
                match tail with
                | n :: rest when not (isFlag n) -> n, rest
                | _ -> "untitled", tail
            
            let userOpts = parseInitArgs argsToParse
            let compiler = 
                match userOpts.TryFind "compiler" with
                | Some c -> c
                | None -> getOrSetupCompiler() // Still interactive fallback for default?
            let standard = userOpts.TryFind "std" |> Option.defaultValue "c++17"
            
            let options = { Name = name; Compiler = compiler; Standard = standard; Arch = "x64"; Type = "exe" }
            initProject options
        else
            // Interactive mode
            let nameArg = 
                match tail with
                | n :: _ -> Some n
                | [] -> None
            
            let options = runInteractiveInit nameArg
            initProject options
        0
        
    | ["build"] ->
        match build() with
        | Ok () -> 
            Console.WriteLine("Build successful.")
            0
        | Error e -> 
            Console.Error.WriteLine($"Build failed: {e}")
            1
            
    | ["run"] ->
        match run() with
        | Ok () -> 0
        | Error e ->
            Console.Error.WriteLine($"Run failed: {e}")
            1
            
    | _ ->
        Console.WriteLine("Usage: flappy <command> [options]")
        Console.WriteLine("Commands:")
        Console.WriteLine("  init [name]           Start interactive setup")
        Console.WriteLine("  init <name> [flags]   Quick setup with flags")
        Console.WriteLine("    --compiler, -c <cmd>")
        Console.WriteLine("    --std, -s <ver>")
        Console.WriteLine("  build                 Build the project")
        Console.WriteLine("  run                   Build and run the project")
        1
