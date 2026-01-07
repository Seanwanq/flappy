module Flappy.Program

open System
open System.IO
open Flappy.Scaffolding
open Flappy.Builder
open Flappy.GlobalConfig
open Flappy.Toolchain
open Flappy.Interactive

let getOrSetupCompiler () =
    match loadConfig () with
    | Some config -> config.DefaultCompiler
    | None ->
        Console.WriteLine "Welcome to Flappy! First-time setup..."
        Console.WriteLine "Scanning for C++ toolchains..."
        let toolchains = getAvailableToolchains ()

        if toolchains.IsEmpty then
            Console.WriteLine "No standard C++ toolchains found (MSVC, g++, clang++)."
            Console.WriteLine "Recommended: Install Visual Studio with C++ workload or MinGW."
            Console.Write "Enter command for your compiler (e.g. g++): "
            let input = Console.ReadLine()

            let compiler =
                if String.IsNullOrWhiteSpace input then
                    "g++"
                else
                    input.Trim()

            saveConfig { DefaultCompiler = compiler }
            compiler
        else
            let options = toolchains |> List.map (fun t -> $"{t.Name} ({t.Command})")

            match select "Select a default compiler:" options 0 with
            | Some selection ->
                let selected =
                    toolchains |> List.find (fun t -> $"{t.Name} ({t.Command})" = selection)

                saveConfig { DefaultCompiler = selected.Command }
                Console.WriteLine $"Selected {selected.Command} as default."
                selected.Command
            | None ->
                Console.WriteLine "Setup cancelled."
                exit 0

let runInteractiveInit (nameArg: string option) : InitOptions option =
    Console.WriteLine "Initializing new Flappy project..."

    // 1. Name
    let name =
        match nameArg with
        | Some n -> n
        | None ->
            Console.Write "Project Name: "
            let input = Console.ReadLine()

            if String.IsNullOrWhiteSpace input then
                "untitled"
            else
                input.Trim()

    // Helper to chain selections
    let rec askSteps () =
        // 2. Compiler
        let toolchains = getAvailableToolchains ()

        let compilerResult =
            if toolchains.IsEmpty then
                Console.Write "Compiler (e.g. g++): "
                let input = Console.ReadLine()

                Some(
                    if String.IsNullOrWhiteSpace(input) then
                        "g++"
                    else
                        input.Trim()
                )
            else
                let opts = toolchains |> List.map (fun t -> t.Command)
                select "Select Compiler:" opts 0

        match compilerResult with
        | None -> None
        | Some compiler ->
            // 3. Standard
            let standards = [ "c++17"; "c++20"; "c++23"; "c++14"; "c++11" ]

            match select "Select C++ Standard:" standards 0 with
            | None -> None
            | Some std ->
                // 4. Arch
                let archs = [ "x64"; "x86"; "arm64" ]

                match select "Select Architecture:" archs 0 with
                | None -> None
                | Some arch ->
                    // 5. Type
                    let types = [ "exe"; "dll" ]

                    match select "Select Project Type:" types 0 with
                    | None -> None
                    | Some type' ->
                        Some
                            {
                                Name = name
                                Compiler = compiler
                                Standard = std
                                Arch = arch
                                Type = type'
                            }

    askSteps ()

let parseInitArgs (args: string list) =
    let rec parse (opts: Map<string, string>) (rest: string list) =
        match rest with
        | [] -> opts
        | "-c" :: val' :: tail
        | "--compiler" :: val' :: tail -> parse (opts.Add("compiler", val')) tail
        | "-s" :: val' :: tail
        | "--std" :: val' :: tail -> parse (opts.Add("std", val')) tail
        | "--git" :: val' :: tail -> parse (opts.Add("git", val')) tail
        | "--url" :: val' :: tail -> parse (opts.Add("url", val')) tail
        | "--path" :: val' :: tail -> parse (opts.Add("path", val')) tail
        | "--tag" :: val' :: tail -> parse (opts.Add("tag", val')) tail
        | "-D" :: val' :: tail
        | "--define" :: val' :: tail ->
            // Support multiple defines by appending with a separator
            let current = opts.TryFind "defines" |> Option.defaultValue ""
            let next = if current = "" then val' else current + "," + val'
            parse (opts.Add("defines", next)) tail
        | head :: tail ->
            Console.WriteLine($"Warning: Unknown argument '{head}' ignored.")
            parse opts tail

    parse Map.empty args

[<EntryPoint>]
let main args =
    let argsList = args |> Array.toList

    match argsList with
    | "init" :: tail ->
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
                | None -> getOrSetupCompiler ()

            let standard = userOpts.TryFind "std" |> Option.defaultValue "c++17"

            let options =
                {
                    Name = name
                    Compiler = compiler
                    Standard = standard
                    Arch = "x64"
                    Type = "exe"
                }

            initProject options
        else
            // Interactive mode
            let nameArg =
                match tail with
                | n :: _ -> Some n
                | [] -> None

            match runInteractiveInit nameArg with
            | Some options -> initProject options
            | None -> Console.WriteLine "Initialization cancelled."

        0

    | [ "build" ] ->
        match build () with
        | Ok() ->
            Console.WriteLine "Build successful."
            0
        | Error e ->
            Console.Error.WriteLine $"Build failed: {e}"
            1

    | [ "run" ] ->
        match run () with
        | Ok() -> 0
        | Error e ->
            Console.Error.WriteLine $"Run failed: {e}"
            1

    | [ "sync" ] ->
        match sync () with
        | Ok() -> 0
        | Error e ->
            Console.Error.WriteLine $"Sync failed: {e}"
            1

    | "cache" :: subArgs ->
        match subArgs with
        | [ "clean" ] ->
            match Flappy.DependencyManager.cleanCache () with
            | Ok() -> 0
            | Error e ->
                Console.Error.WriteLine(e)
                1
        | _ ->
            Console.WriteLine "Usage: flappy cache <command>"
            Console.WriteLine "Commands:"
            Console.WriteLine "  clean         Clear the global dependency cache"
            1

    | "add" :: name :: rest ->
        // Simple manual parsing for add command
        let args = parseInitArgs rest // Reusing existing parser logic which produces Map<string, string>

        let git = args.TryFind "git"
        let url = args.TryFind "url"
        let path = args.TryFind "path"
        let tag = args.TryFind "tag"

        let defines =
            match args.TryFind "defines" with
            | Some d ->
                d.Split ','
                |> Array.map (fun x -> "\"" + x.Trim() + "\"")
                |> String.concat ", "
            | None -> ""

        let definesPart = if defines = "" then "" else $", defines = [{defines}]"

        // Validation: exactly one source
        let sources = [ git; url; path ] |> List.choose id

        if sources.Length <> 1 then
            Console.Error.WriteLine
                "Error: Please specify exactly one source: --git <url>, --url <url>, or --path <path>"

            1
        else
            let tomlLine =
                match git with
                | Some g ->
                    match tag with
                    | Some t -> $"{name} = {{ git = \"{g}\", tag = \"{t}\"{definesPart} }}"
                    | None -> $"{name} = {{ git = \"{g}\"{definesPart} }}"
                | None ->
                    match url with
                    | Some u -> $"{name} = {{ url = \"{u}\"{definesPart} }}"
                    | None ->
                        match path with
                        | Some p -> $"{name} = {{ path = \"{p}\"{definesPart} }}"
                        | None -> "" // Should not happen

            match Config.addDependency "flappy.toml" name tomlLine with
            | Ok() ->
                Console.WriteLine $"Added dependency '{name}' to flappy.toml."

                match sync () with
                | Ok() ->
                    Console.WriteLine "Dependency installed and locked."
                    0
                | Error e ->
                    Console.Error.WriteLine $"Dependency added, but sync failed: {e}"
                    1
            | Error e ->
                Console.Error.WriteLine $"Failed to add dependency: {e}"
                1

    | "remove" :: [ name ]
    | "rm" :: [ name ] ->
        match Config.removeDependency "flappy.toml" name with
        | Ok() ->
            Console.WriteLine $"Removed dependency '{name}' from flappy.toml."

            match sync () with
            | Ok() ->
                // Cleanup the linked directory in packages/
                let pkgDir = Path.Combine("packages", name)

                if Directory.Exists pkgDir then
                    try
                        Directory.Delete(pkgDir, true)
                    with _ ->
                        ()

                0
            | Error e ->
                Console.Error.WriteLine $"Dependency removed from toml, but sync failed: {e}"
                1
        | Error e ->
            Console.Error.WriteLine $"Failed to remove dependency: {e}"
            1

    | _ ->
        Console.WriteLine "Usage: flappy <command> [options]"
        Console.WriteLine "Commands:"
        Console.WriteLine "  init [name]           Start interactive setup"
        Console.WriteLine "  init <name> [flags]   Quick setup with flags"
        Console.WriteLine "    --compiler, -c <cmd>"
        Console.WriteLine "    --std, -s <ver>"
        Console.WriteLine "  add <name> [flags]    Add a dependency"
        Console.WriteLine "    --git <url> [--tag <tag>]"
        Console.WriteLine "    --url <url>"
        Console.WriteLine "    --path <path>"
        Console.WriteLine "    --define, -D <macro>"
        Console.WriteLine "  remove <name>         Remove a dependency (alias: rm)"
        Console.WriteLine "  sync                  Install dependencies and update flappy.lock"
        Console.WriteLine "  build                 Build the project"
        Console.WriteLine "  run                   Build and run the project"
        Console.WriteLine "  cache clean           Clear the global dependency cache"
        1
