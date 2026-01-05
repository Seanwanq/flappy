module Flappy.Builder

open System
open System.IO
open System.Diagnostics
open Flappy.Config
open Flappy.VsDevCmd

let runCommand (cmd: string) (args: string) =
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    
    // Debug logging
    // printfn "DEBUG: Executing '%s' with args '%s'" cmd args

    try
        use p = Process.Start(psi)
        p.WaitForExit()
        let output = p.StandardOutput.ReadToEnd()
        let error = p.StandardError.ReadToEnd()
        if p.ExitCode = 0 then
            printfn "%s" output
            // Some tools write to stderr even on success (like some linkers), but usually we only show if error?
            // For now, print error if not empty just in case it contains warnings
            if not (String.IsNullOrWhiteSpace error) then
                eprintfn "%s" error
            Ok ()
        else
            printfn "%s" output
            eprintfn "%s" error
            Error (sprintf "Command failed with exit code %d" p.ExitCode)
    with
    | ex -> Error (sprintf "Failed to run command: %s" ex.Message)

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
                    // Ensure output directory exists
                    let outDir = Path.GetDirectoryName(config.Build.Output)
                    if not (String.IsNullOrEmpty outDir) && not (Directory.Exists outDir) then
                        Directory.CreateDirectory outDir |> ignore

                    let compiler = config.Build.Compiler
                    
                    // Logic for arguments depends on the compiler style.
                    // GCC/Clang: -std=c++17 -o output source
                    // MSVC: /std:c++17 /Fe:output source
                    
                    let isMsvc = compiler.ToLower().Contains("cl") || compiler.ToLower() = "msvc"
                    
                    let args = 
                        if isMsvc then
                           // Basic mapping for MSVC
                           // Note: /std:c++17 might require /Zc:__cplusplus in some versions, but stick to basic.
                           // Output flag is /Fe:path or /Fe path
                           // Also add /EHsc for exceptions
                           sprintf "/std:%s /EHsc /Fe:%s %s" config.Build.Standard config.Build.Output sources
                        else
                           sprintf "-std=%s -o %s %s" config.Build.Standard config.Build.Output sources

                    printfn "Compiling [%s]..." config.Package.Name
                    
                    // Check if we need to patch for MSVC environment
                    let finalCmd, finalArgs = 
                        match patchCommandForMsvc compiler args with
                        | Some (c, a) -> (c, a)
                        | None -> 
                            // Fallback for non-msvc or if msvc env detection failed (assume in path)
                            (compiler, args)

                    printfn "Running: %s %s" finalCmd finalArgs
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
                printfn "Running %s..." exePath
                runCommand exePath ""
            else
                Error (sprintf "Executable not found: %s" exePath)
        | Error e -> Error e