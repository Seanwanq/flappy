module Flappy.Program

open System
open System.IO
open System.Runtime.InteropServices
open Flappy.Config
open Flappy.Scaffolding
open Flappy.Builder
open Flappy.GlobalConfig
open Flappy.Toolchain
open Flappy.Interactive
open Spectre.Console

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
            match select "Select a default compiler:" options 0 with
            | Some selection ->
                let selected = toolchains |> List.find (fun t -> $"{t.Name} ({t.Command})" = selection)
                saveConfig { DefaultCompiler = selected.Command }
                Console.WriteLine($"Selected {selected.Command} as default.")
                selected.Command
            | None ->
                Console.WriteLine("Setup cancelled.")
                exit 0

let runInteractiveInit (nameArg: string option) : InitOptions option =
    Console.WriteLine("Initializing new Flappy project...")
    
    let name =
        match nameArg with
        | Some n -> n
        | None -> 
            Console.Write("Project Name: ")
            let input = Console.ReadLine()
            if String.IsNullOrWhiteSpace(input) then "untitled" else input.Trim()

    let rec askSteps () =
        let langs = ["c++"; "c"]
        match select "Select Project Language:" langs 0 with
        | None -> None
        | Some lang ->
            let toolchains = getAvailableToolchains()
            let compilerResult =
                if toolchains.IsEmpty then
                    Console.Write("Compiler (e.g. g++): ")
                    let input = Console.ReadLine()
                    Some (if String.IsNullOrWhiteSpace(input) then "g++" else input.Trim())
                else
                    let opts = toolchains |> List.map (fun t -> t.Command)
                    select "Select Compiler:" opts 0
            
            match compilerResult with
            | None -> None
            | Some compiler ->
                let standards = if lang = "c" then ["c11"; "c17"; "c99"; "c89"] else ["c++17"; "c++20"; "c++23"; "c++14"; "c++11"]
                match select "Select Standard:" standards 0 with
                | None -> None
                | Some std ->
                    let archs = ["x64"; "x86"; "arm64"]
                    match select "Select Architecture:" archs 0 with
                    | None -> None
                    | Some arch ->
                        let typeOptions = ["Executable (exe)"; "Static Library (lib)"; "Dynamic Library (dll)"]
                        match select "Select Project Type:" typeOptions 0 with
                        | None -> None
                        | Some typeSelection ->
                            let type' =
                                if typeSelection.Contains("(exe)") then "exe"
                                elif typeSelection.Contains("(lib)") then "lib"
                                else "dll"
                            Some { Name = name; Compiler = compiler; Language = lang; Standard = std; Arch = arch; Type = type' }

    askSteps()

let runXPlatWizard () =
    if not (File.Exists "flappy.toml") then
        Log.error "Error" "No flappy.toml found in current directory."
        1
    else
        Log.info "XPlat" "Configuring cross-platform build targets..."
        let platforms = ["windows"; "linux"; "macos"]
        match select "Select target platform to configure:" platforms 0 with
        | None -> 0
        | Some platform ->
            let currentPlatform =
                if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
                elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
                elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "macos"
                else "unknown"
            
            let compilerResult =
                if platform = currentPlatform then
                    let toolchains = getAvailableToolchains()
                    if toolchains.IsEmpty then
                        Console.Write($"No compilers found. Enter command for {platform}: ")
                        Some (Console.ReadLine().Trim())
                    else
                        let opts = toolchains |> List.map (fun t -> t.Command)
                        select $"Select compiler for {platform}:" opts 0
                else
                    let suggestions =
                        match platform with 
                        | "windows" -> ["cl"; "clang-cl"; "g++"]
                        | "linux" -> ["g++"; "clang++"]
                        | "macos" -> ["clang++"; "g++"]
                        | _ -> []
                    let opts = suggestions @ ["(Enter custom command)"]
                    match select $"Choose a compiler for {platform}:" opts 0 with
                    | None -> None
                    | Some choice when choice.Contains("custom") ->
                        Console.Write($"Enter compiler command for {platform}: ")
                        Some (Console.ReadLine().Trim())
                    | Some choice -> Some choice
            
            match compilerResult with
            | None -> 0
            | Some compiler ->
                match Config.updateProfileConfig "flappy.toml" platform None compiler with
                | Ok () -> 
                    Log.info "Success" $"Updated flappy.toml with {platform} configuration."
                    0
                | Error e -> 
                    Log.error "Failed" $"Failed to update config: {e}"
                    1

let interactiveConfigure (platform: string) =
    let currentPlatform =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "macos"
        else "unknown"
    
    let compilerResult =
        if platform = currentPlatform then
            let toolchains = getAvailableToolchains()
            if toolchains.IsEmpty then
                AnsiConsole.Ask<string>($"No compilers found. Enter command for [cyan]{platform}[/]:")
                |> Some
            else
                let opts = toolchains |> List.map (fun t -> t.Command)
                select $"Select compiler for {platform}:" opts 0
        else
            let suggestions =
                match platform with 
                | "windows" -> ["cl"; "clang-cl"; "g++"]
                | "linux" -> ["g++"; "clang++"]
                | "macos" -> ["clang++"; "g++"]
                | _ -> []
            let opts = suggestions @ ["(Enter custom command)"]
            match select $"Choose a compiler for {platform}:" opts 0 with
            | None -> None
            | Some choice when choice.Contains("custom") ->
                AnsiConsole.Ask<string>($"Enter compiler command for [cyan]{platform}[/]:")
                |> Some
            | Some choice -> Some choice
    
    match compilerResult with
    | None -> Error "Configuration cancelled."
    | Some compiler ->
        match Config.updateProfileConfig "flappy.toml" platform None compiler with
        | Ok () -> 
            Log.info "Success" $"Updated flappy.toml with {platform} configuration."
            Ok ()
        | Error e -> Error e

let runProfileWizard () =
    if not (File.Exists "flappy.toml") then
        Log.error "Error" "No flappy.toml found."
        1
    else
        let name = AnsiConsole.Ask<string>("Enter Profile Name (e.g. [green]rpi-arm64[/]):")
        let platforms = ["windows"; "linux"; "macos"; "any"]
        match select "Select target platform for this profile:" platforms 0 with
        | None -> 0
        | Some platform ->
            let platArg = if platform = "any" then None else Some platform
            let toolchains = getAvailableToolchains()
            let compilerResult =
                if toolchains.IsEmpty then
                    AnsiConsole.Ask<string>($"Enter compiler command for [cyan]{name}[/]:") |> Some
                else
                    let opts = toolchains |> List.map (fun t -> t.Command)
                    select $"Select compiler for {name} ({platform}):" opts 0
            
            match compilerResult with
            | None -> 0
            | Some compiler ->
                match Config.updateProfileConfig "flappy.toml" name platArg compiler with
                | Ok () ->
                    Log.info "Success" $"Added profile '[cyan]{name}[/]' to flappy.toml."
                    0
                | Error e ->
                    Log.error "Error" e
                    1

let ensureProfileDefined (profile: string option) =
    if not (File.Exists "flappy.toml") then Error "flappy.toml not found."
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content profile with
        | Error e -> Error e
        | Ok config ->
            let compiler = config.Build.Compiler
            let isPath = compiler.Contains("/") || compiler.Contains("\\")
            
            let compilerValid = 
                if isPath then File.Exists compiler
                else 
                    let isMsvc = List.contains (compiler.ToLower()) ["cl"; "cl.exe"; "msvc"; "clang-cl"; "lib"]
                    if isMsvc && (Config.getVsInstallations()).Length > 0 then true
                    else checkCommand compiler

            if config.IsProfileDefined && compilerValid then Ok profile
            else
                let target = 
                    match profile with
                    | Some p -> p
                    | None ->
                        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
                        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
                        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "macos"
                        else "unknown"
                
                let reason = if not config.IsProfileDefined then "No configuration found" else $"Compiler not found at '[red]{compiler}[/]'"
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {reason} for profile/platform '[cyan]{target}[/]'.")
                
                if not AnsiConsole.Profile.Capabilities.Interactive then
                    Error $"Non-interactive mode: Compiler path is invalid and cannot prompt for configuration."
                elif AnsiConsole.Confirm("Would you like to configure (or re-configure) it now?") then
                    match interactiveConfigure target with
                    | Ok () -> Ok profile
                    | Error e -> Error e
                else
                    if not compilerValid then Error $"Compiler check failed: {compiler}"
                    else Ok profile

let parseInitArgs (args: string list) =
    let rec parse (opts: Map<string, string>) (rest: string list) =
        match rest with
        | [] -> opts
        | "-c" :: val' :: tail | "--compiler" :: val' :: tail ->
            parse (opts.Add("compiler", val')) tail
        | "-l" :: val' :: tail | "--language" :: val' :: tail ->
            parse (opts.Add("language", val')) tail
        | "-s" :: val' :: tail | "--std" :: val' :: tail ->
            parse (opts.Add("std", val')) tail
        | "--git" :: val' :: tail -> parse (opts.Add("git", val')) tail
        | "--url" :: val' :: tail -> parse (opts.Add("url", val')) tail
        | "--path" :: val' :: tail -> parse (opts.Add("path", val')) tail
        | "--tag" :: val' :: tail -> parse (opts.Add("tag", val')) tail
        | "-D" :: val' :: tail | "--define" :: val' :: tail ->
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
    
    // Auto-detect project root for project-related commands
    let projectCommands = ["build"; "run"; "test"; "sync"; "update"; "add"; "remove"; "rm"; "clean"; "compdb"; "xplat"; "profile"]
    match argsList with
    | cmd :: _ when List.contains cmd projectCommands ->
        match Config.findProjectRoot (Directory.GetCurrentDirectory()) with
        | Some root -> 
            if root <> Directory.GetCurrentDirectory() then
                Directory.SetCurrentDirectory(root)
        | None -> () // Fallback to current dir, commands will fail gracefully if flappy.toml is missing
    | _ -> ()

    match argsList with
    | "init" :: tail ->
        let isFlag (s: string) = s.StartsWith("-")
        let hasFlags = tail |> List.exists isFlag
        if hasFlags then
            let name, argsToParse =
                match tail with
                | n :: rest when not (isFlag n) -> n, rest
                | _ -> "untitled", tail
            let userOpts = parseInitArgs argsToParse
            let compiler =
                match userOpts.TryFind "compiler" with
                | Some c -> c
                | None -> getOrSetupCompiler()
            let language = userOpts.TryFind "language" |> Option.defaultValue "c++"
            let defaultStd = if language = "c" then "c11" else "c++17"
            let standard = userOpts.TryFind "std" |> Option.defaultValue defaultStd
            initProject { Name = name; Compiler = compiler; Language = language; Standard = standard; Arch = "x64"; Type = "exe" }
        else
            let nameArg = match tail with | n :: _ -> Some n | [] -> None
            match runInteractiveInit nameArg with
            | Some options -> initProject options
            | None -> Console.WriteLine("Initialization cancelled.")
        0
    | "build" :: tail ->
        let profileArg, otherArgs = 
            match tail with
            | "-t" :: t :: rest | "--target" :: t :: rest -> Some t, rest
            | head :: rest when not (head.StartsWith("-")) -> Some head, rest
            | _ -> None, tail
        
        match ensureProfileDefined profileArg with
        | Error e -> 
            if e <> "Build cancelled by user." then Console.Error.WriteLine(e)
            1
        | Ok profileArg ->
            let profile = if List.contains "--release" otherArgs then Release else Debug
            let sw = Diagnostics.Stopwatch.StartNew()
            match build profile profileArg with
            | Ok () -> 
                sw.Stop()
                let profileName = if profile = Release then "release" else "dev"
                Log.info "Finished" $"{profileName} target(s) in {sw.Elapsed.TotalSeconds:F2}s"
                
                // Auto-generate compilation database
                match Flappy.Generator.generate profileArg with
                | Ok () -> ()
                | Error e -> Log.warn "CompDB" ("Failed to update compile_commands.json: " + e)
                
                0
            | Error e -> 
                Console.Error.WriteLine($"Build failed: {e}")
                1
    | "run" :: tail ->
        let profileArg, otherArgs = 
            match tail with
            | "-t" :: t :: rest | "--target" :: t :: rest -> Some t, rest
            | head :: rest when not (head.StartsWith("-")) -> Some head, rest
            | _ -> None, tail

        match ensureProfileDefined profileArg with
        | Error e -> 
            if e <> "Build cancelled by user." then Console.Error.WriteLine(e)
            1
        | Ok profileArg ->
            let profile = if List.contains "--release" otherArgs then Release else Debug
            let extraArgs =
                match otherArgs |> List.tryFindIndex (fun x -> x = "--") with
                | Some idx -> otherArgs |> List.skip (idx + 1) |> String.concat " "
                | None -> ""
            
            // Generate compdb BEFORE running (or as part of build)
            match Flappy.Generator.generate profileArg with
            | Ok () -> ()
            | Error e -> Log.warn "CompDB" ("Failed to update compile_commands.json: " + e)

            let sw = Diagnostics.Stopwatch.StartNew()
            match run profile extraArgs profileArg with
            | Ok () -> 
                sw.Stop()
                0
            | Error e ->
                Console.Error.WriteLine($"Run failed: {e}")
                1
    | "test" :: tail ->
        let profileArg, otherArgs = 
            match tail with
            | "-t" :: t :: rest | "--target" :: t :: rest -> Some t, rest
            | head :: rest when not (head.StartsWith("-")) -> Some head, rest
            | _ -> None, tail

        match ensureProfileDefined profileArg with
        | Error e -> 
            if e <> "Build cancelled by user." then Console.Error.WriteLine(e)
            1
        | Ok profileArg ->
            let profile = if List.contains "--release" otherArgs then Release else Debug
            let extraArgs =
                match otherArgs |> List.tryFindIndex (fun x -> x = "--") with
                | Some idx -> otherArgs |> List.skip (idx + 1) |> String.concat " "
                | None -> ""
            let sw = Diagnostics.Stopwatch.StartNew()
            match runTest profile extraArgs profileArg with
            | Ok () -> sw.Stop(); 0
            | Error e ->
                Console.Error.WriteLine($"Test failed: {e}")
                1
    | "update" :: tail ->
        if not (File.Exists "flappy.toml") then
            Log.error "Error" "flappy.toml not found."
            1
        else
            let content = File.ReadAllText "flappy.toml"
            match Config.parse content None with
            | Error e -> Log.error "Error" e; 1
            | Ok config ->
                let targetName = match tail with | name :: _ -> Some name | [] -> None
                let depsToUpdate = 
                    match targetName with
                    | Some name -> config.Dependencies |> List.filter (fun d -> d.Name = name)
                    | None -> config.Dependencies
                
                if depsToUpdate.IsEmpty then
                    Log.warn "Update" "No matching dependencies found to update."
                    0
                else
                    let mutable success = true
                    for dep in depsToUpdate do
                        match Flappy.DependencyManager.update dep with
                        | Ok() -> ()
                        | Error e -> Log.error "Failed" e; success <- false
                    
                    if success then
                        Log.info "Success" "Dependencies updated. Re-syncing..."
                        match sync() with
                        | Ok () -> 0
                        | Error e -> Log.error "Sync Failed" e; 1
                    else 1
    | "compdb" :: tail ->
        let profileArg, otherArgs = 
            match tail with
            | head :: rest when not (head.StartsWith("-")) -> Some head, rest
            | _ -> None, tail
        match Flappy.Generator.generate profileArg with
        | Ok () -> 0
        | Error e ->
            Console.Error.WriteLine($"CompDB failed: {e}")
            1
    | ["profile"; "add"] ->
        runProfileWizard ()
    | ["xplat"] ->
        runXPlatWizard ()
    | ["clean"] ->
        if not (File.Exists "flappy.toml") then
            Console.Error.WriteLine("flappy.toml not found.")
            1
        else
            let content = File.ReadAllText "flappy.toml"
            match Config.parse content None with
            | Error e -> 
                Console.Error.WriteLine($"Failed to parse flappy.toml: {e}")
                1
            | Ok config ->
                let outDir = Path.GetDirectoryName(config.Build.Output)
                if not (String.IsNullOrEmpty outDir) && Directory.Exists outDir then
                    try Directory.Delete(outDir, true); Console.WriteLine($"Cleaned {outDir}") with _ -> ()
                if Directory.Exists "obj" then
                    try Directory.Delete("obj", true); Console.WriteLine("Cleaned obj/") with _ -> ()
                Console.WriteLine("Cleanup complete.")
                0
    | ["sync"] ->
        match sync() with
        | Ok () -> 0
        | Error e ->
            Console.Error.WriteLine($"Sync failed: {e}")
            1
    | ["cache"; "clean"] ->
        match Flappy.DependencyManager.cleanCache() with
        | Ok () -> 0
        | Error e ->
            Console.Error.WriteLine(e)
            1
    | "add" :: name :: rest ->
        let args = parseInitArgs rest 
        let git = args.TryFind "git"
        let url = args.TryFind "url"
        let path = args.TryFind "path"
        let tag = args.TryFind "tag"
        let defines =
            match args.TryFind "defines" with
            | Some d -> d.Split(',') |> Array.map (fun x -> """ + x.Trim() + """) |> String.concat ", "
            | None -> ""
        let definesPart = if defines = "" then "" else $", defines = [{defines}]"
        let sources = [git; url; path] |> List.choose id
        if sources.Length <> 1 then
            Console.Error.WriteLine("Error: Please specify exactly one source: --git <url>, --url <url>, or --path <path>")
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
                        | None -> "" 
            match Config.addDependency "flappy.toml" name tomlLine with
            | Ok () ->
                Log.info "Added" $"dependency '{name}' to flappy.toml."
                match sync() with
                | Ok () -> 
                    Log.info "Success" "Dependency installed and locked."
                    0
                | Error e -> 
                    Log.error "Failed" $"Sync failed: {e}"
                    Log.warn "Rollback" $"Removing '{name}' from flappy.toml due to failure."
                    Config.removeDependency "flappy.toml" name |> ignore
                    1
            | Error e ->
                Log.error "Error" $"Failed to add dependency: {e}"
                1
    | "remove" :: [name] | "rm" :: [name] ->
        match Config.removeDependency "flappy.toml" name with
        | Ok () ->
            Console.WriteLine($"Removed dependency '{name}' from flappy.toml.")
            match sync() with
            | Ok () -> 
                let pkgDir = Path.Combine("packages", name)
                if Directory.Exists(pkgDir) then
                    try Directory.Delete(pkgDir, true) with _ -> ()
                0
            | Error e -> 
                Console.Error.WriteLine($"Sync failed: {e}")
                1
        | Error e ->
            Console.Error.WriteLine($"Failed to remove dependency: {e}")
            1
    | _ ->
        Console.WriteLine("Usage: flappy <command> [options]")
        Console.WriteLine("Commands:")
        Console.WriteLine("  init [name]           Start interactive setup")
        Console.WriteLine("  init <name> [flags]   Quick setup with flags")
        Console.WriteLine("    --compiler, -c <cmd>")
        Console.WriteLine("    --language, -l <c|c++>")
        Console.WriteLine("    --std, -s <ver>")
        Console.WriteLine("  add <name> [flags]    Add a dependency")
        Console.WriteLine("    --git <url> [--tag <tag>]")
        Console.WriteLine("    --url <url>")
        Console.WriteLine("    --path <path>")
        Console.WriteLine("    --define, -D <macro>")
        Console.WriteLine("  remove <name>         Remove a dependency (alias: rm)")
        Console.WriteLine("  sync                  Install dependencies and update flappy.lock")
        Console.WriteLine("  update [name]         Update dependency to latest version and re-build")
        Console.WriteLine("  build [profile]       Build the project")
        Console.WriteLine("    --release           Build in release mode")
        Console.WriteLine("    --target, -t <name> Specify build profile/target")
        Console.WriteLine("  run [profile]         Build and run the project")
        Console.WriteLine("    --release           Run in release mode")
        Console.WriteLine("    --target, -t <name> Specify build profile/target")
        Console.WriteLine("  test [profile]        Build and run tests")
        Console.WriteLine("    --release           Test in release mode")
        Console.WriteLine("    --target, -t <name> Specify build profile/target")
        Console.WriteLine("  profile add           Add a custom build profile (interactive)")
        Console.WriteLine("  compdb [profile]      Generate compilation database (compile_commands.json)")
        Console.WriteLine("  xplat                 Configure toolchains for other platforms")
        Console.WriteLine("  clean                 Remove build artifacts (bin/ and obj/)")
        Console.WriteLine("  cache clean           Clear the global dependency cache")
        1
