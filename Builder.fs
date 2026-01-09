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
            if not (String.IsNullOrWhiteSpace filtered) then Console.WriteLine(filtered)
            if not (String.IsNullOrWhiteSpace error) then Console.Error.WriteLine(error)
            Ok ()
        else
            Console.WriteLine(output)
            Console.Error.WriteLine(error)
            Error ("Command failed with exit code " + string p.ExitCode)
    with ex -> Error ("Failed to run command: " + ex.Message)

let installDependencies (deps: Dependency list) (profile: BuildProfile) (compiler: string) : Result<DependencyMetadata list, string> = 
    let results = deps |> List.map (fun d -> 
        match install d profile compiler with 
        | Ok meta -> Ok meta 
        | Error e -> Error ("Failed to install " + d.Name + ": " + e))
    let failures = results |> List.choose (function Error e -> Some e | _ -> None)
    if failures.Length > 0 then Error (String.concat "\n" failures)
    else Ok (results |> List.choose (function Ok p -> Some p | _ -> None))

let sync () = 
    if not (File.Exists "flappy.toml") then Error "flappy.toml not found."
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content None with
        | Error e -> Error ("Failed to parse configuration: " + e)
        | Ok config ->
            Log.info "Syncing" ("[" + config.Package.Name + "]")
            match installDependencies config.Dependencies Debug config.Build.Compiler with
            | Error e -> Error e
            | Ok results ->
                let lockEntries = results |> List.mapi (fun i meta ->
                    let dep = config.Dependencies.[i]
                    let sourceStr = match dep.Source with | Git (url, _) -> url | Url url -> url | Local path -> path
                    { Name = dep.Name; Source = sourceStr; Resolved = meta.Resolved })
                Config.saveLock "flappy.lock" { Entries = lockEntries }
                Log.info "Locked" (string lockEntries.Length + " dependencies")
                Ok ()

type BuildContext = {
    Config: FlappyConfig
    Compiler: string
    IsMsvc: bool
    AllSources: string list
    IncludePaths: string list
    Defines: string list
    ObjBaseDir: string
    ProfileFlags: string
    ArchFlags: string
    TypeFlags: string
    CustomFlags: string
    DepResults: DependencyMetadata list
}

let prepareBuild (profile: BuildProfile) (targetProfile: string option) : Result<BuildContext, string> =
    if not (File.Exists "flappy.toml") then Error "flappy.toml not found."
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content targetProfile with
        | Error e -> Error ("Failed to parse configuration: " + e)
        | Ok config ->
            if not (Directory.Exists "src") then Error "src directory not found."
            else
                let lang = config.Build.Language.ToLower()
                let extensions = if lang = "c" then ["*.c"] else ["*.cpp"; "*.cc"; "*.cxx"; "*.c"; "*.ixx"; "*.cppm"]
                let allSources = extensions |> List.collect (fun ext -> Directory.GetFiles("src", ext, SearchOption.AllDirectories) |> Array.toList)
                if allSources.IsEmpty then Error "No source files found in src/."
                else
                    let compiler = config.Build.Compiler
                    match installDependencies config.Dependencies profile compiler with
                    | Error e -> Error e
                    | Ok depResults ->
                        let mutable includePaths = depResults |> List.collect (fun m -> m.IncludePaths)
                        if Directory.Exists "include" then includePaths <- Path.GetFullPath("include") :: includePaths
                        let allDefines = config.Build.Defines @ (config.Dependencies |> List.collect (fun d -> d.Defines))
                        let outDir = Path.GetDirectoryName(config.Build.Output)
                        if not (String.IsNullOrEmpty outDir) && not (Directory.Exists outDir) then Directory.CreateDirectory outDir |> ignore
                        let profileStr = match profile with | Debug -> "debug" | Release -> "release"
                        let objBaseDir = Path.Combine("obj", config.Build.Arch, profileStr)
                        if not (Directory.Exists objBaseDir) then Directory.CreateDirectory objBaseDir |> ignore
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
                        
                        let customFlags = config.Build.Flags |> String.concat " "

                        Ok {
                            Config = config
                            Compiler = compiler
                            IsMsvc = isMsvc
                            AllSources = allSources
                            IncludePaths = includePaths
                            Defines = allDefines
                            ObjBaseDir = objBaseDir
                            ProfileFlags = profileFlags
                            ArchFlags = archFlags
                            TypeFlags = typeFlags
                            CustomFlags = customFlags
                            DepResults = depResults
                        }

let build (profile: BuildProfile) (targetProfile: string option) = 
    match prepareBuild profile targetProfile with
    | Error e -> Error e
    | Ok ctx ->
        let includeFlags = if ctx.IsMsvc then ctx.IncludePaths |> List.map (fun p -> "/I\"" + p + "\"") |> String.concat " " else ctx.IncludePaths |> List.map (fun p -> "-I\"" + p + "\"") |> String.concat " "
        let defineFlags = if ctx.IsMsvc then ctx.Defines |> List.map (fun d -> "/D" + d) |> String.concat " " else ctx.Defines |> List.map (fun d -> "-D" + d) |> String.concat " "
        
        let isInterface (path: string) = let ext = Path.GetExtension(path).ToLower() in ext = ".ixx" || ext = ".cppm"
        let interfaces = ctx.AllSources |> List.filter isInterface
        let implementations = ctx.AllSources |> List.filter (isInterface >> not)
        
        let compileFile (src: string) = async {
            let relPath = Path.GetRelativePath("src", src)
            let objExt = if ctx.IsMsvc then ".obj" else ".o"
            let objPath = Path.Combine(ctx.ObjBaseDir, relPath + objExt)
            let objDir = Path.GetDirectoryName(objPath)
            if not (Directory.Exists objDir) then Directory.CreateDirectory objDir |> ignore
            let srcInfo = FileInfo(src)
            let objInfo = FileInfo(objPath)
            if not objInfo.Exists || srcInfo.LastWriteTime > objInfo.LastWriteTime then
                Log.info "Compiling" relPath
                let extraModuleFlags = if ctx.IsMsvc && isInterface src then "/interface" else ""
                let compileArgs = 
                    if ctx.IsMsvc then 
                        let pdbPath = Path.Combine(ctx.ObjBaseDir, "vc.pdb") 
                        "/c /nologo /FS /std:" + ctx.Config.Build.Standard + " /EHsc " + ctx.ProfileFlags + " " + includeFlags + " " + defineFlags + " " + ctx.ArchFlags + " " + ctx.CustomFlags + " " + extraModuleFlags + " /Fo:\"" + objPath + "\" /Fd:\"" + pdbPath + "\" \"" + src + "\"" 
                    else 
                        "-c -std=" + ctx.Config.Build.Standard + " " + ctx.ProfileFlags + " " + includeFlags + " " + defineFlags + " " + ctx.ArchFlags + " " + ctx.CustomFlags + " -o \"" + objPath + "\" \"" + src + "\""
                
                let finalCmd, finalArgs = match patchCommandForMsvc ctx.Compiler compileArgs ctx.Config.Build.Arch with | Some (c, a) -> (c, a) | None -> (ctx.Compiler, compileArgs)
                let res = runCommand finalCmd finalArgs
                match res with | Error e -> return Some (objPath, e) | Ok () -> return None
            else return None }
            
        let interfaceResults = interfaces |> List.map compileFile |> Async.Parallel |> Async.RunSynchronously
        let interfaceFailures = interfaceResults |> Array.choose id
        if interfaceFailures.Length > 0 then Error (snd interfaceFailures.[0])
        else
            let implResults = implementations |> List.map compileFile |> Async.Parallel |> Async.RunSynchronously
            let implFailures = implResults |> Array.choose id
            if implFailures.Length > 0 then Error (snd implFailures.[0])
            else
                let objFiles = ctx.AllSources |> List.map (fun src -> let relPath = Path.GetRelativePath("src", src) in let objExt = if ctx.IsMsvc then ".obj" else ".o" in Path.Combine(ctx.ObjBaseDir, relPath + objExt))
                let buildType = ctx.Config.Build.Type.ToLower()
                let outputName = 
                    if buildType = "dll" || buildType = "shared" then (if ctx.IsMsvc then ctx.Config.Build.Output + ".dll" elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then ctx.Config.Build.Output + ".dylib" else ctx.Config.Build.Output + ".so") 
                    elif buildType = "lib" || buildType = "static" then (if ctx.IsMsvc then ctx.Config.Build.Output + ".lib" else ctx.Config.Build.Output + ".a") 
                    else (if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && not (ctx.Config.Build.Output.EndsWith(".exe")) then ctx.Config.Build.Output + ".exe" else ctx.Config.Build.Output)
                
                let outInfo = FileInfo(outputName)
                let latestObjTime = if objFiles.IsEmpty then DateTime.MinValue else objFiles |> Seq.map (fun f -> FileInfo(f).LastWriteTime) |> Seq.max
                if outInfo.Exists && outInfo.LastWriteTime > latestObjTime then Log.info "Up-to-date" ("[" + ctx.Config.Package.Name + "]"); Ok () 
                else
                    let isLibrary = buildType = "lib" || buildType = "static"
                    let allObjs = objFiles |> Seq.map (fun f -> "\"" + f + "\"") |> String.concat " "
                    let depLibs = if not isLibrary then ctx.DepResults |> List.collect (fun (meta: DependencyMetadata) -> if not meta.Libs.IsEmpty then meta.Libs else let pkgRoot = (let inc = if meta.IncludePaths.IsEmpty then "." else meta.IncludePaths.[0] in if inc.EndsWith("include") || inc.EndsWith("include" + string Path.DirectorySeparatorChar) then Path.GetDirectoryName(inc) else inc) in let libExts = if ctx.IsMsvc then [ "*.lib" ] else [ "*.a"; "*.so" ] in libExts |> List.collect (fun ext -> if Directory.Exists pkgRoot then Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList else [])) |> List.distinct |> List.map (fun l -> "\"" + l + "\"") |> String.concat " " else ""
                    let linkCmd, linkArgs = if isLibrary then (if ctx.IsMsvc then "lib", "/NOLOGO /OUT:\"" + outputName + "\" " + allObjs else "ar", "rcs \"" + outputName + "\" " + allObjs) else (let extraLinkFlags = if ctx.IsMsvc then (match profile with Debug -> "/DEBUG" | Release -> "") else "" in let baseArgs = if ctx.IsMsvc then ctx.TypeFlags + " " + allObjs + " " + depLibs + " " + extraLinkFlags + " " + ctx.CustomFlags + " /Fe:\"" + ctx.Config.Build.Output + "\" /link /PDB:\"" + ctx.Config.Build.Output + ".pdb\"" else ctx.TypeFlags + " " + ctx.ArchFlags + " " + allObjs + " " + depLibs + " " + ctx.CustomFlags + " -o \"" + ctx.Config.Build.Output + "\"" in ctx.Compiler, baseArgs)
                    Log.info (if isLibrary then "Archiving" else "Linking") outputName
                    let finalLinkCmd, finalLinkArgs = match patchCommandForMsvc linkCmd linkArgs ctx.Config.Build.Arch with | Some (c, a) -> (c, a) | None -> (linkCmd, linkArgs)
                    match runCommand finalLinkCmd finalLinkArgs with
                    | Error e -> Error e
                    | Ok () ->
                        if not isLibrary then
                            let outDir = if String.IsNullOrEmpty(Path.GetDirectoryName(outputName)) then "." else Path.GetDirectoryName(outputName)
                            for (meta: DependencyMetadata) in ctx.DepResults do for includePath in meta.IncludePaths do let pkgRoot = if includePath.EndsWith("include") || includePath.EndsWith("include" + string Path.DirectorySeparatorChar) then Path.GetDirectoryName(includePath) else includePath in let dlls = [ "*.dll"; "*.so"; "*.dylib" ] |> List.collect (fun ext -> if Directory.Exists pkgRoot then Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList else []) in for dll in dlls do let dest = Path.Combine(outDir, Path.GetFileName(dll)) in if not (File.Exists(dest)) || FileInfo(dll).LastWriteTime > FileInfo(dest).LastWriteTime then Log.info "Copying" (Path.GetFileName(dll)); File.Copy(dll, dest, true)
                        Ok ()

let run (profile: BuildProfile) (extraArgs: string) (targetProfile: string option) =
    match build profile targetProfile with
    | Error e -> Error e
    | Ok () ->
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content targetProfile with
        | Ok config ->
            let exePath = if Environment.OSVersion.Platform = PlatformID.Win32NT then (if config.Build.Type = "dll" || config.Build.Type = "shared" then "" elif not (config.Build.Output.EndsWith(".exe")) then config.Build.Output + ".exe" else config.Build.Output) else config.Build.Output
            if String.IsNullOrEmpty exePath || not (File.Exists exePath) then (if config.Build.Type = "exe" then Error ("Executable not found: " + exePath) else Log.info "Skipping" "Output is a library."; Ok ()) 
            else Log.info "Running" (exePath + " " + extraArgs); runCommand exePath extraArgs
        | Error e -> Error e

let buildTest (profile: BuildProfile) (targetProfile: string option) =
    if not (File.Exists "flappy.toml") then Error "flappy.toml not found."
    else
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content targetProfile with
        | Error e -> Error ("Failed to parse configuration: " + e)
        | Ok config ->
            match config.Test with
            | None -> Error "No [test] section found in flappy.toml."
            | Some testConfig ->
                // 1. Resolve Sources
                let resolveSource (pattern: string) =
                    let dir = Path.GetDirectoryName(pattern)
                    let searchPattern = Path.GetFileName(pattern)
                    let searchDir = if String.IsNullOrEmpty(dir) then "." else dir
                    if Directory.Exists searchDir then
                        Directory.GetFiles(searchDir, searchPattern, SearchOption.AllDirectories) |> Array.toList
                    else []
                
                let testSources = testConfig.Sources |> List.collect resolveSource
                if testSources.IsEmpty then Error "No test source files found."
                else
                    let compiler = config.Build.Compiler
                    // 2. Install Dependencies
                    match installDependencies config.Dependencies profile compiler with
                    | Error e -> Error e
                    | Ok depResults ->
                        let mutable includePaths = depResults |> List.collect (fun m -> m.IncludePaths)
                        if Directory.Exists "include" then includePaths <- Path.GetFullPath("include") :: includePaths
                        
                        let allDefines = config.Build.Defines @ testConfig.Defines @ (config.Dependencies |> List.collect (fun d -> d.Defines))
                        
                        // Output setup
                        let outDir = Path.GetDirectoryName(testConfig.Output)
                        if not (String.IsNullOrEmpty outDir) && not (Directory.Exists outDir) then Directory.CreateDirectory outDir |> ignore
                        
                        let profileStr = match profile with | Debug -> "debug" | Release -> "release"
                        let objBaseDir = Path.Combine("obj", "test", config.Build.Arch, profileStr)
                        if not (Directory.Exists objBaseDir) then Directory.CreateDirectory objBaseDir |> ignore
                        
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
                        
                        let includeFlags = if isMsvc then includePaths |> List.map (fun p -> "/I\"" + p + "\"") |> String.concat " " else includePaths |> List.map (fun p -> "-I\"" + p + "\"") |> String.concat " "
                        let defineFlags = if isMsvc then allDefines |> List.map (fun d -> "/D" + d) |> String.concat " " else allDefines |> List.map (fun d -> "-D" + d) |> String.concat " "
                        let customFlags = config.Build.Flags |> String.concat " "

                        // 3. Compile
                        let compileFile (src: string) = async {
                            let relPath = Path.GetRelativePath(".", src) // Use relative to root for tests
                            let objExt = if isMsvc then ".obj" else ".o"
                            let objPath = Path.Combine(objBaseDir, Path.GetFileName(src) + objExt) // Simple flat obj structure for now to avoid deep nesting issues
                            
                            let srcInfo = FileInfo(src)
                            let objInfo = FileInfo(objPath)
                            if not objInfo.Exists || srcInfo.LastWriteTime > objInfo.LastWriteTime then
                                Log.info "Compiling" relPath
                                let compileArgs = if isMsvc then let pdbPath = Path.Combine(objBaseDir, "vc.pdb") in "/c /nologo /FS /std:" + config.Build.Standard + " /EHsc " + profileFlags + " " + includeFlags + " " + defineFlags + " " + archFlags + " " + customFlags + " /Fo:\"" + objPath + "\" /Fd:\"" + pdbPath + "\" \"" + src + "\"" else "-c -std=" + config.Build.Standard + " " + profileFlags + " " + includeFlags + " " + defineFlags + " " + archFlags + " " + customFlags + " -o \"" + objPath + "\" \"" + src + "\""
                                let finalCmd, finalArgs = match patchCommandForMsvc compiler compileArgs config.Build.Arch with | Some (c, a) -> (c, a) | None -> (compiler, compileArgs)
                                let res = runCommand finalCmd finalArgs
                                match res with | Error e -> return Some (objPath, e) | Ok () -> return None
                            else return None }
                        
                        let results = testSources |> List.map compileFile |> Async.Parallel |> Async.RunSynchronously
                        let failures = results |> Array.choose id
                        if failures.Length > 0 then Error (snd failures.[0])
                        else
                            // 4. Link
                            let objFiles = testSources |> List.map (fun src -> let objExt = if isMsvc then ".obj" else ".o" in Path.Combine(objBaseDir, Path.GetFileName(src) + objExt))
                            
                            let outputName = 
                                if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && not (testConfig.Output.EndsWith(".exe")) then testConfig.Output + ".exe" else testConfig.Output
                                
                            let allObjs = objFiles |> Seq.map (fun f -> "\"" + f + "\"") |> String.concat " "
                            
                            // Auto-link main project lib if applicable
                            let projectLib = 
                                let buildType = config.Build.Type.ToLower()
                                if buildType = "lib" || buildType = "static" then
                                    let libName = if isMsvc then config.Build.Output + ".lib" else config.Build.Output + ".a"
                                    if File.Exists libName then Some libName else None
                                else None
                            
                            let depLibs = 
                                let libs = depResults |> List.collect (fun (meta: DependencyMetadata) -> if not meta.Libs.IsEmpty then meta.Libs else let pkgRoot = (let inc = if meta.IncludePaths.IsEmpty then "." else meta.IncludePaths.[0] in if inc.EndsWith("include") || inc.EndsWith("include" + string Path.DirectorySeparatorChar) then Path.GetDirectoryName(inc) else inc) in let libExts = if isMsvc then [ "*.lib" ] else [ "*.a"; "*.so" ] in libExts |> List.collect (fun ext -> if Directory.Exists pkgRoot then Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList else [])) 
                                let all = match projectLib with | Some l -> l :: libs | None -> libs
                                all |> List.distinct |> List.map (fun l -> "\"" + l + "\"") |> String.concat " "

                            let extraLinkFlags = if isMsvc then (match profile with Debug -> "/DEBUG" | Release -> "") else ""
                            let linkCmd, linkArgs = 
                                if isMsvc then 
                                    compiler, "/Fe:\"" + outputName + "\" " + allObjs + " " + depLibs + " " + extraLinkFlags + " " + customFlags + " /link /PDB:\"" + outputName + ".pdb\""
                                else 
                                    compiler, allObjs + " " + depLibs + " " + customFlags + " " + archFlags + " -o \"" + outputName + "\""

                            Log.info "Linking" outputName
                            let finalLinkCmd, finalLinkArgs = match patchCommandForMsvc linkCmd linkArgs config.Build.Arch with | Some (c, a) -> (c, a) | None -> (linkCmd, linkArgs)
                            match runCommand finalLinkCmd finalLinkArgs with
                            | Error e -> Error e
                            | Ok () ->
                                // Copy DLLs
                                let outDir = if String.IsNullOrEmpty(Path.GetDirectoryName(outputName)) then "." else Path.GetDirectoryName(outputName)
                                for (meta: DependencyMetadata) in depResults do for includePath in meta.IncludePaths do let pkgRoot = if includePath.EndsWith("include") || includePath.EndsWith("include" + string Path.DirectorySeparatorChar) then Path.GetDirectoryName(includePath) else includePath in let dlls = [ "*.dll"; "*.so"; "*.dylib" ] |> List.collect (fun ext -> if Directory.Exists pkgRoot then Directory.GetFiles(pkgRoot, ext, SearchOption.AllDirectories) |> Array.toList else []) in for dll in dlls do let dest = Path.Combine(outDir, Path.GetFileName(dll)) in if not (File.Exists(dest)) || FileInfo(dll).LastWriteTime > FileInfo(dest).LastWriteTime then Log.info "Copying" (Path.GetFileName(dll)); File.Copy(dll, dest, true)
                                Ok ()

let runTest (profile: BuildProfile) (extraArgs: string) (targetProfile: string option) =
    match buildTest profile targetProfile with
    | Error e -> Error e
    | Ok () ->
        let content = File.ReadAllText "flappy.toml"
        match Config.parse content targetProfile with
        | Ok config ->
            match config.Test with
            | Some testConfig ->
                let exePath = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && not (testConfig.Output.EndsWith(".exe")) then testConfig.Output + ".exe" else testConfig.Output
                if File.Exists exePath then
                    Log.info "Running" (exePath + " " + extraArgs)
                    runCommand exePath extraArgs
                else Error ("Test executable not found: " + exePath)
            | None -> Error "No [test] configuration."
        | Error e -> Error e