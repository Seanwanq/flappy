module Flappy.Builder

open System
open System.IO
open System.Diagnostics
open Flappy.Config
open Flappy.VsDevCmd
open Flappy.DependencyManager

let runCommand (cmd: string) (args: string) = 
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    
    try
        use p = Process.Start(psi)
        p.WaitForExit()
        let output = p.StandardOutput.ReadToEnd()
        let error = p.StandardError.ReadToEnd()
        if p.ExitCode = 0 then
            Console.WriteLine(output)
            if not (String.IsNullOrWhiteSpace error) then
                Console.Error.WriteLine(error)
            Ok ()
        else
            Console.WriteLine(output)
            Console.Error.WriteLine(error)
            Error (sprintf "Command failed with exit code %d" p.ExitCode)
    with 
    | ex -> Error (sprintf "Failed to run command: %s" ex.Message)

let installDependencies (deps: Dependency list) : Result<string list, string> = 
    let results = 
        deps 
        |> List.map (fun d -> 
            match install d with 
            | Ok path -> Ok path
            | Error e -> Error $"Failed to install {d.Name}: {e}"
        )
    
    let failures = results |> List.choose (function Error e -> Some e | _ -> None)
    
    if failures.Length > 0 then
        Error (String.concat "\n" failures)
    else
        Ok (results |> List.choose (function Ok p -> Some p | _ -> None))

let build () = 
    if not (File.Exists "flappy.toml") then
        Error "flappy.toml not found. Are you in a flappy project?"
    else 
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content with 
        | Error e -> Error (sprintf "Failed to parse configuration: %s" e)
        | Ok config -> 
            if not (Directory.Exists "src") then
                Error "src directory not found."
            else 
                let sources = Directory.GetFiles("src", "*.cpp", SearchOption.AllDirectories) |> String.concat " "
                if String.IsNullOrWhiteSpace sources then
                    Error "No .cpp files found in src/."
                else 
                    // Install dependencies
                    match installDependencies config.Dependencies with 
                    | Error e -> Error e
                    | Ok includePaths -> 
                        // Ensure output directory exists
                        let outDir = Path.GetDirectoryName(config.Build.Output)
                        if not (String.IsNullOrEmpty outDir) && not (Directory.Exists outDir) then 
                            Directory.CreateDirectory outDir |> ignore

                        let compiler = config.Build.Compiler
                        let isMsvc = compiler.ToLower().Contains("cl") || compiler.ToLower() = "msvc" || compiler.ToLower().Contains("clang-cl")
                        
                        // Generate Include Flags
                        let includeFlags = 
                            if isMsvc then 
                                includePaths |> List.map (fun p -> sprintf "/I\"%s\"" p) |> String.concat " "
                            else 
                                includePaths |> List.map (fun p -> sprintf "-I\"%s\"" p) |> String.concat " "

                        let args = 
                            if isMsvc then 
                               sprintf "/std:%s /EHsc %s /Fe:%s %s" config.Build.Standard includeFlags config.Build.Output sources
                            else 
                               sprintf "-std=%s %s -o %s %s" config.Build.Standard includeFlags config.Build.Output sources

                        Console.WriteLine($"Compiling [{config.Package.Name}]...")
                        
                        let finalCmd, finalArgs = 
                            match patchCommandForMsvc compiler args with 
                            | Some (c, a) -> (c, a)
                            | None -> (compiler, args)

                        Console.WriteLine($"Running: {finalCmd} {finalArgs}")
                        runCommand finalCmd finalArgs

let run () = 
    match build() with 
    | Error e -> Error e
    | Ok () -> 
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content with 
        | Ok config -> 
            let exePath = 
                if Environment.OSVersion.Platform = PlatformID.Win32NT && not (config.Build.Output.EndsWith(".exe")) then 
                    config.Build.Output + ".exe"
                else 
                    config.Build.Output
            
            if File.Exists exePath then 
                Console.WriteLine($"Running {exePath}...")
                runCommand exePath ""
            else 
                Error (sprintf "Executable not found: %s" exePath)
        | Error e -> Error e
