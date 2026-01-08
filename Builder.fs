module Flappy.Builder

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open Flappy.Config
open Flappy.DependencyManager

let runCommand (cmd: string) (args: string) = 
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    
    try
        use p = Process.Start(psi)
        p.WaitForExit()
        let output = p.StandardOutput.ReadToEnd()
        let error = p.StandardError.ReadToEnd()
        
        let filter (s: string) =
            s.Split('\n')
            |> Seq.filter (fun line -> 
                let l = line.Trim()
                not (l.StartsWith("Microsoft (R)")) && 
                not (l.StartsWith("Copyright (C)")) &&
                not (l.Contains("Developer Command Prompt")) &&
                not (l.Contains("vcvarsall.bat")) &&
                not (String.IsNullOrWhiteSpace l))
            |> String.concat "\n"

        if p.ExitCode = 0 then
            let filtered = filter output
            if not (String.IsNullOrWhiteSpace filtered) then
                Console.WriteLine(filtered)
            if not (String.IsNullOrWhiteSpace error) then
                Console.Error.WriteLine(error)
            Ok ()
        else
            Console.WriteLine(output)
            Console.Error.WriteLine(error)
            Error $"Command failed with exit code {p.ExitCode}"
    with
    | ex -> Error $"Failed to run command: {ex.Message}"

let installDependencies (deps: Dependency list) (profile: BuildProfile) : Result<DependencyMetadata list, string> = 
    let results = 
        deps 
        |> List.map (fun d -> 
            match install d profile with
            | Ok meta -> Ok meta
            | Error e -> Error $"Failed to install {d.Name}: {e}"
        )
    
    let failures = results |> List.choose (function Error e -> Some e | _ -> None)
    
    if failures.Length > 0 then
        Error (String.concat "\n" failures)
    else
        Ok (results |> List.choose (function Ok p -> Some p | _ -> None))

let sync () = 
    if not (File.Exists "flappy.toml") then
        Error "flappy.toml not found."
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content with
        | Error e -> Error $"Failed to parse configuration: {e}"
        | Ok config ->
            Log.info "Syncing" $"[{config.Package.Name}]"
            match installDependencies config.Dependencies Debug with
            | Error e -> Error e
            | Ok results ->
                let lockEntries = 
                    results 
                    |> List.mapi (fun i meta ->
                        let dep = config.Dependencies.[i]
                        let sourceStr = 
                            match dep.Source with
                            | Git (url, _) -> url
                            | Url url -> url
                            | Local path -> path
                        { Name = dep.Name; Source = sourceStr; Resolved = meta.Resolved }
                    )
                Config.saveLock "flappy.lock" { Entries = lockEntries }
                Log.info "Locked" $"{lockEntries.Length} dependencies"
                Ok ()

let getSources (dir: string) = 
    let extensions = [ "*.cpp"; "*.c"; "*.cc"; "*.cxx" ]
    extensions 
    |> List.collect (fun ext -> Directory.GetFiles(dir, ext, SearchOption.AllDirectories) |> Array.toList)

let build (profile: BuildProfile) = 
    if not (File.Exists "flappy.toml") then
        Error "flappy.toml not found. Are you in a flappy project?"
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content with
        | Error e -> Error $"Failed to parse configuration: {e}"
        | Ok config ->
            if not (Directory.Exists "src") then
                Error "src directory not found."
            else
                let sources = getSources "src"
                if sources.IsEmpty then
                    Error "No source files found in src/."
                else
                    match installDependencies config.Dependencies profile with
                    | Error e -> Error e
                    | Ok depResults ->
                        let mutable includePaths = depResults |> List.collect (fun m -> m.IncludePaths)
                        if Directory.Exists "include" then
                            includePaths <- Path.GetFullPath("include") :: includePaths

                        let allDefines = 
                            config.Build.Defines @ (config.Dependencies |> List.collect (fun d -> d.Defines))
                        
                        let outDir = Path.GetDirectoryName(config.Build.Output)
                        let fullOutDir = if String.IsNullOrEmpty(outDir) then "." else outDir
                        if not (Directory.Exists fullOutDir) then
                            Directory.CreateDirectory fullOutDir |> ignore

                        let profileStr = match profile with Debug -> "debug" | Release -> "release"
                        let objBaseDir = Path.Combine("obj", config.Build.Arch, profileStr)
                        if not (Directory.Exists objBaseDir) then
                            Directory.CreateDirectory objBaseDir |> ignore

                        let compiler = config.Build.Compiler
                        let isMsvc = compiler.ToLower().Contains("cl") || compiler.ToLower() = "msvc" || compiler.ToLower().Contains("clang-cl")
                        
                        let profileFlags = 
                            match profile with
                            | Debug -> if isMsvc then "/Zi /Od /MDd" else "-g -O0"
                            | Release -> if isMsvc then "/O2 /DNDEBUG /MD" else "-O3 -DNDEBUG"

                        let archFlags = 
                            if isMsvc then "" 
                            else
                                match config.Build.Arch.ToLower() with
                                | "x86" -> "-m32"
                                | "x64" -> "-m64"
                                | _ -> ""

                        let typeFlags =
                            match config.Build.Type.ToLower() with
                            | "dll" | "shared" | "dynamic" -> if isMsvc then "/LD" else "-shared -fPIC"
                            | _ -> ""

                        let includeFlags = 
                            if isMsvc then
                                includePaths |> List.map (fun p -> $"/I\"{p}\"") |> String.concat " "
                            else
                                includePaths |> List.map (fun p -> $"-I\"{p}\"") |> String.concat " "
                        
                        let defineFlags =
                            if isMsvc then
                                allDefines |> List.map (fun d -> $"/D{d}") |> String.concat " "
                            else
                                allDefines |> List.map (fun d -> $"-D{d}") |> String.concat " "

                        let customFlags = config.Build.Flags |> String.concat " "

                        let compileTasks = 
                            sources |> List.map (fun src ->
                                async {
                                    let relPath = Path.GetRelativePath("src", src)
                                    let objExt = if isMsvc then ".obj" else ".o"
                                    let objPath = Path.Combine(objBaseDir, relPath + objExt)
                                    let objDir = Path.GetDirectoryName(objPath)
                                    if not (Directory.Exists objDir) then Directory.CreateDirectory objDir |> ignore
                                    
                                    let srcInfo = FileInfo(src)
                                    let objInfo = FileInfo(objPath)
                                    
                                    if not objInfo.Exists || srcInfo.LastWriteTime > objInfo.LastWriteTime then
                                        Log.info "Compiling" relPath
                                        let compileArgs = 
                                            if isMsvc then
                                                let pdbPath = Path.Combine(objBaseDir, "vc.pdb")
                                                $"/c /nologo /FS /std:{config.Build.Standard} /EHsc {profileFlags} {includeFlags} {defineFlags} {archFlags} {customFlags} /Fo:\"{objPath}\" /Fd:\"{pdbPath}\" \"{src}\""
                                            else
                                                $"-c -std:{config.Build.Standard} {profileFlags} {includeFlags} {defineFlags} {archFlags} {customFlags} -o \"{objPath}\" \"{src}\""
                                        
                                        let finalCmd, finalArgs = 
                                            match patchCommandForMsvc compiler compileArgs config.Build.Arch with
                                            | Some (c, a) -> (c, a)
                                            | None -> (compiler, compileArgs)

                                        match runCommand finalCmd finalArgs with
                                        | Error e -> return Some (objPath, e)
                                        | Ok () -> return None
                                    else
                                        return None
                                }
                            )

                        let compileResults = compileTasks |> Async.Parallel |> Async.RunSynchronously
                        let failures = compileResults |> Array.choose id
                        
                        if failures.Length > 0 then
                            let _, firstError = failures.[0]
                            Error firstError
                        else
                            let objFiles = 
                                sources |> List.map (fun src ->
                                    let relPath = Path.GetRelativePath("src", src)
                                    let objExt = if isMsvc then ".obj" else ".o"
                                    Path.Combine(objBaseDir, relPath + objExt)
                                )

                            let outputName = 
                                let buildType = config.Build.Type.ToLower()
                                if buildType = "dll" || buildType = "shared" then
                                    if isMsvc then config.Build.Output + ".dll" 
                                    elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then config.Build.Output + ".dylib"
                                    else config.Build.Output + ".so"
                                elif buildType = "lib" || buildType = "static" then
                                    if isMsvc then config.Build.Output + ".lib"
                                    else config.Build.Output + ".a"
                                else
                                    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && not (config.Build.Output.EndsWith(".exe")) then
                                        config.Build.Output + ".exe"
                                    else
                                        config.Build.Output

                            let outInfo = FileInfo(outputName)
                            let latestObjTime = 
                                if objFiles.IsEmpty then DateTime.MinValue
                                else objFiles |> Seq.map (fun f -> FileInfo(f).LastWriteTime) |> Seq.max
                            
                            if outInfo.Exists && outInfo.LastWriteTime > latestObjTime then
                                Log.info "Up-to-date" $"[{config.Package.Name}]"
                                Ok ()
                            else
                                // Linking / Archiving
                                let buildType = config.Build.Type.ToLower()
                                let isLib = buildType = "lib" || buildType = "static"
                                let allObjs = objFiles |> Seq.map (fun f -> $"\"{f}\"" ) |> String.concat " "
                                
                                // Find dependency libraries to link against
                                let depLibs = 
                                    if not isLib then
                                        depResults |> List.collect (fun meta ->
                                            if not meta.Libs.IsEmpty then
                                                meta.Libs
                                            else
                                                // Fallback to recursive scanning
                                                let pkgRoot = 
                                                    let includePath = if meta.IncludePaths.IsEmpty then "." else meta.IncludePaths.[0]
                                                    if includePath.EndsWith("include") || includePath.EndsWith("include" + string Path.DirectorySeparatorChar) then
                                                        Path.GetDirectoryName(includePath)
                                                    else
                                                        includePath
                                                
                                                let libExtensions = if isMsvc then [ "*.lib" ] else [ "*.a"; "*.so" ]
                                                libExtensions |> List.collect (fun ext -> 
                                                    if Directory.Exists pkgRoot then
                                                        Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList
                                                    else [])
                                        ) |> List.distinct |> List.map (fun l -> $"\"{l}\"" ) |> String.concat " "
                                    else ""

                                let linkCmd, linkArgs =
                                    if isLib then
                                        if isMsvc then
                                            "lib", $"/NOLOGO /OUT:\"{outputName}\" {allObjs}"
                                        else
                                            "ar", $"rcs \"{outputName}\" {allObjs}"
                                    else
                                        let extraLinkFlags = 
                                            if isMsvc then
                                                match profile with 
                                                | Debug -> "/DEBUG" 
                                                | Release -> ""
                                            else ""

                                        let baseArgs = 
                                            if isMsvc then
                                                $"{typeFlags} {allObjs} {depLibs} {extraLinkFlags} {customFlags} /Fe:\"{config.Build.Output}\" /link /PDB:\"{config.Build.Output}.pdb\""
                                            else
                                                $"{typeFlags} {archFlags} {allObjs} {depLibs} {customFlags} -o \"{config.Build.Output}\""
                                        compiler, baseArgs
                                
                                Log.info (if isLib then "Archiving" else "Linking") outputName
                                let finalLinkCmd, finalLinkArgs =
                                    match patchCommandForMsvc linkCmd linkArgs config.Build.Arch with
                                    | Some (c, a) -> (c, a)
                                    | None -> (linkCmd, linkArgs)
                                
                                match runCommand finalLinkCmd finalLinkArgs with
                                | Error e -> Error e
                                | Ok () ->
                                    if not isLib then
                                        let outDir = if String.IsNullOrEmpty(Path.GetDirectoryName(outputName)) then "." else Path.GetDirectoryName(outputName)
                                        for meta in depResults do
                                            for includePath in meta.IncludePaths do
                                                let pkgRoot = 
                                                    if includePath.EndsWith("include") || includePath.EndsWith("include" + string Path.DirectorySeparatorChar) then
                                                        Path.GetDirectoryName(includePath)
                                                    else
                                                        includePath
                                                
                                                let dllExtensions = [ "*.dll"; "*.so"; "*.dylib" ]
                                                let dlls = dllExtensions |> List.collect (fun ext -> 
                                                    if Directory.Exists pkgRoot then
                                                        Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList
                                                    else [])
                                                
                                                for dll in dlls do
                                                    let dest = Path.Combine(outDir, Path.GetFileName(dll))
                                                    if not (File.Exists(dest)) || FileInfo(dll).LastWriteTime > FileInfo(dest).LastWriteTime then
                                                        Log.info "Copying" (Path.GetFileName(dll))
                                                        File.Copy(dll, dest, true)
                                    Ok ()

let run (profile: BuildProfile) (extraArgs: string) =
    match build profile with
    | Error e -> Error e
    | Ok () ->
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content with
        | Ok config ->
            let exePath = 
                if Environment.OSVersion.Platform = PlatformID.Win32NT then
                    if config.Build.Type = "dll" || config.Build.Type = "shared" then "" 
                    elif not (config.Build.Output.EndsWith(".exe")) then config.Build.Output + ".exe"
                    else config.Build.Output
                else
                    config.Build.Output
            
            if String.IsNullOrEmpty exePath || not (File.Exists exePath) then
                if config.Build.Type = "exe" then Error (sprintf "Executable not found: %s" exePath)
                else 
                    Log.info "Skipping" "Output is a library."
                    Ok ()
            else
                Log.info "Running" $"{exePath} {extraArgs}"
                runCommand exePath extraArgs
        | Error e -> Error e