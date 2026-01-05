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
            Console.WriteLine("Found the following toolchains:")
            toolchains |> List.iteri (fun i t -> Console.WriteLine($"[{i + 1}] {t.Name} ({t.Command})"))
            
            Console.Write($"Select a default compiler [1-{toolchains.Length}]: ")
            let input = Console.ReadLine()
            match Int32.TryParse(input) with
            | true, idx when idx >= 1 && idx <= toolchains.Length ->
                let selected = toolchains.[idx - 1].Command
                saveConfig { DefaultCompiler = selected }
                Console.WriteLine($"Selected {selected} as default.")
                selected
            | _ ->
                let defaultCompiler = toolchains.[0].Command
                Console.WriteLine($"Invalid selection. Defaulting to {defaultCompiler}.")
                saveConfig { DefaultCompiler = defaultCompiler }
                defaultCompiler

let parseInitArgs (args: string list) =
    let rec parse (opts: Map<string, string>) (rest: string list) =
        match rest with
        | [] -> opts
        | "-c" :: val' :: tail | "--compiler" :: val' :: tail ->
            parse (opts.Add("compiler", val')) tail
        | "-s" :: val' :: tail | "--std" :: val' :: tail ->
            parse (opts.Add("std", val')) tail
        | head :: tail ->
            // Ignore unknown args or handle as name if name not set? 
            // Name is handled before calling this.
            Console.WriteLine($"Warning: Unknown argument '{head}' ignored.")
            parse opts tail
            
    parse Map.empty args

[<EntryPoint>]
let main args =
    let argsList = args |> Array.toList
    match argsList with
    | "init" :: name :: tail -> 
        let userOpts = parseInitArgs tail
        
        let compiler = 
            match userOpts.TryFind "compiler" with
            | Some c -> c
            | None -> getOrSetupCompiler()
            
        let standard =
            match userOpts.TryFind "std" with
            | Some s -> s
            | None -> "c++17" // Default standard

        let options = { Name = name; Compiler = compiler; Standard = standard }
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
        Console.WriteLine("  init <name> [flags]   Create a new project")
        Console.WriteLine("    --compiler, -c <cmd>  Specify compiler (e.g. clang++, g++)")
        Console.WriteLine("    --std, -s <ver>       Specify C++ standard (e.g. c++20)")
        Console.WriteLine("  build                 Build the project")
        Console.WriteLine("  run                   Build and run the project")
        1