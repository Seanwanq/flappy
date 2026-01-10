module Flappy.DependencyManager

open System
open System.IO
open System.Net.Http
open System.Diagnostics
open System.Text
open System.Runtime.InteropServices
open System.Threading
open System.Text.RegularExpressions
open Flappy.Config

let getGlobalCacheDir () =
    let basePath =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            Environment.GetFolderPath Environment.SpecialFolder.ApplicationData
        else
            let xdg = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            if not (String.IsNullOrWhiteSpace xdg) then xdg
            else
                let home = Environment.GetEnvironmentVariable "HOME"
                if not (String.IsNullOrWhiteSpace home) then Path.Combine(home, ".cache")
                else Environment.GetFolderPath Environment.SpecialFolder.ApplicationData
    
    let dir = Path.Combine(basePath, "flappy", "cache")
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir

let getLocalPackagesDir () =
    let dir = "packages"
    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    dir

/// Pure F# FNV-1a 32-bit hash implementation. 
/// Used for generating unique cache keys from URLs without dependency on OpenSSL/libssl.
let computeHash (input: string) =
    let offsetBasis = 2166136261u
    let prime = 16777619u
    let bytes = Encoding.UTF8.GetBytes input
    let mutable hash = offsetBasis
    for b in bytes do
        hash <- hash ^^^ (uint32 b)
        hash <- hash * prime
    hash.ToString("x8")

let executeCommand (cmd: string) (args: string) (workingDir: string) (env: Map<string, string> option) : Result<unit, string> =
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    if not (String.IsNullOrEmpty workingDir) then
        psi.WorkingDirectory <- workingDir

    env |> Option.iter (fun e ->
        for kv in e do
            if psi.EnvironmentVariables.ContainsKey kv.Key then
                psi.EnvironmentVariables.[kv.Key] <- kv.Value
            else
                psi.EnvironmentVariables.Add(kv.Key, kv.Value))

    try
        use p = new Process()
        p.StartInfo <- psi
        let output = StringBuilder()
        let error = StringBuilder()
        
        p.OutputDataReceived.Add(fun e -> if not (String.IsNullOrEmpty e.Data) then output.AppendLine(e.Data) |> ignore)
        p.ErrorDataReceived.Add(fun e -> if not (String.IsNullOrEmpty e.Data) then error.AppendLine(e.Data) |> ignore)
        
        if not (p.Start()) then 
            Error $"Failed to start {cmd}"
        else
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            p.WaitForExit()
            if p.ExitCode <> 0 then
                Error $"{cmd} failed:\nSTDOUT: {output.ToString()}\nSTDERR: {error.ToString()}"
            else
                Ok()
    with ex -> Error $"Failed to run {cmd}: {ex.Message}"

let executeGit (args: string) (workingDir: string) : Result<unit, string> =
    executeCommand "git" args workingDir None

let getGitCommit (workingDir: string) : string =
    try
        let psi = ProcessStartInfo(FileName = "git", Arguments = "rev-parse HEAD", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
        psi.WorkingDirectory <- workingDir
        use p = Process.Start psi
        if isNull p then "unknown"
        else
            let output = p.StandardOutput.ReadToEnd().Trim()
            p.WaitForExit()
            if p.ExitCode = 0 then output else "unknown"
    with _ -> "unknown"

let runCmd (args: string) =
    let psi = ProcessStartInfo(FileName = "cmd.exe", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    try
        use p = Process.Start psi
        if not (isNull p) then
            p.WaitForExit()
            p.ExitCode = 0
        else
            false
    with _ -> false

let createLink (target: string) (link: string) =
    if Directory.Exists link || File.Exists link then
        try Directory.Delete(link, false) with _ -> ()
    
    if not (Directory.Exists link) then
        try
            Directory.CreateSymbolicLink(link, target) |> ignore
        with _ ->
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let args = $"/c mklink /J \"{link}\" \"{target}\""
                if not (runCmd args) then
                    Console.WriteLine $"Warning: Failed to link {link} -> {target}."
            else
                let psi = ProcessStartInfo(FileName = "ln", Arguments = $"-s \"{target}\" \"{link}\"", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
                try
                    use p = Process.Start psi
                    if not (isNull p) then
                        p.WaitForExit() |> ignore
                with _ -> ()

let downloadFile (url: string) (destPath: string) =
    try
        use client = new HttpClient()
        let content = client.GetByteArrayAsync(url).Result
        File.WriteAllBytes(destPath, content)
        Ok()
    with ex -> Error ex.Message

let computeDependencyHash (dep: Dependency) (path: string) =
    let commit = if Directory.Exists(Path.Combine(path, ".git")) then getGitCommit path else "unknown"
    let buildCmd = dep.BuildCmd |> Option.defaultValue ""
    let defines = dep.Defines |> String.concat ";"
    let input = $"{commit}|{buildCmd}|{defines}"
    computeHash input

let buildDependency (dep: Dependency) (path: string) (profile: BuildProfile) (compiler: string) (prevMetas: DependencyMetadata list) =
    let profileStr = match profile with Debug -> "debug" | Release -> "release"
    
    // Construct Environment Variables for Injection
    let mutable env = Map.empty
    env <- env.Add("CC", compiler)
    env <- env.Add("CXX", compiler)
    
    let isMsvc = compiler.ToLower().Contains("cl") || compiler.ToLower() = "msvc" || compiler.ToLower().Contains("clang-cl")
    
    for meta in prevMetas do
        let name = meta.Name.ToUpper().Replace("-", "_")
        let incs = meta.IncludePaths |> String.concat (if isMsvc then ";" else ":")
        let libs = meta.Libs |> String.concat (if isMsvc then ";" else ":")
        env <- env.Add($"FLAPPY_DEP_{name}_INCLUDE", incs)
        env <- env.Add($"FLAPPY_DEP_{name}_LIB", libs)
        
        // Standard Injection
        if isMsvc then
            let existingInc = Environment.GetEnvironmentVariable("INCLUDE")
            let existingLib = Environment.GetEnvironmentVariable("LIB")
            env <- env.Add("INCLUDE", incs + (if String.IsNullOrEmpty existingInc then "" else ";" + existingInc))
            env <- env.Add("LIB", libs + (if String.IsNullOrEmpty existingLib then "" else ";" + existingLib))
        else
            let existingCpath = Environment.GetEnvironmentVariable("CPATH")
            let existingLibPath = Environment.GetEnvironmentVariable("LIBRARY_PATH")
            env <- env.Add("CPATH", incs + (if String.IsNullOrEmpty existingCpath then "" else ":" + existingCpath))
            env <- env.Add("LIBRARY_PATH", libs + (if String.IsNullOrEmpty existingLibPath then "" else ":" + existingLibPath))

    match dep.BuildCmd with
    | Some cmd ->
        let stateFile = Path.Combine(path, ".flappy_build_state")
        let currentHash = computeDependencyHash dep path
        
        let shouldBuild =
            if File.Exists stateFile then
                let storedHash = File.ReadAllText(stateFile).Trim()
                storedHash <> currentHash
            else true

        if not shouldBuild then
            Log.info "Skip" $"{dep.Name} (Up-to-date)"
            Ok()
        else
            Log.info "Build" $"Custom command for {dep.Name}"
            let finalCmd, finalArgs = 
                match patchCommandForMsvc "cmd.exe" $"/c {cmd}" "x64" with
                | Some(c, a) -> c, a
                | None -> "cmd.exe", $"/c {cmd}"
            match executeCommand finalCmd finalArgs path (Some env) with
            | Ok () ->
                try File.WriteAllText(stateFile, currentHash) with _ -> ()
                Ok ()
            | Error e -> Error e
    | None ->
        if File.Exists(Path.Combine(path, "flappy.toml")) then
            // Sub-flappy handles its own incremental build via obj/<arch>/<profile>
            // We just need to trigger it.
            Log.info "Build" $"Flappy build for {dep.Name}"
            let exePath = Process.GetCurrentProcess().MainModule.FileName
            let args = (if profile = Release then "build --release" else "build") + " --no-deps"
            executeCommand exePath args path (Some env)
        elif File.Exists(Path.Combine(path, "CMakeLists.txt")) then
            // ISOLATION: Each profile gets its own build directory
            let buildDir = Path.Combine(path, "flappy_build", profileStr)
            
            // Check if already built for this specific profile
            let libExts = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then [ "*.lib"; "*.a" ] else [ "*.a"; "*.so"; "*.dylib" ]
            let hasLibs = 
                if Directory.Exists buildDir then
                    libExts |> List.exists (fun p -> Directory.GetFiles(buildDir, p, SearchOption.AllDirectories).Length > 0)
                else false

            if hasLibs then
                Ok() // Skip: Already built for this profile
            else
                if not (Directory.Exists buildDir) then
                    Directory.CreateDirectory buildDir |> ignore
                Log.info "Configure" $"{dep.Name} (CMake {profileStr})"
                
                let cmakeBuildType = if profile = Release then "Release" else "Debug"
                let cmakeArgs = $"-S \"{path}\" -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={cmakeBuildType} -DCMAKE_CXX_COMPILER=\"{compiler}\""
                match executeCommand "cmake" cmakeArgs path (Some env) with
                | Error e -> Error e
                | Ok() -> 
                    Log.info "Compile" $"{dep.Name} (CMake {profileStr})"
                    executeCommand "cmake" $"--build \"{buildDir}\" --config {cmakeBuildType}" path (Some env)
        else
            Ok()

let resolveDependencyMetadata (dep: Dependency) (packageDir: string) (resolved: string) =
    let isFlappy = File.Exists(Path.Combine(packageDir, "flappy.toml"))
    
    let scanFiles root patterns =
        patterns |> List.collect (fun p -> 
            if Directory.Exists root then Directory.GetFiles(root, p, SearchOption.AllDirectories) |> Array.toList 
            else [])

    // 1. Resolve Include Paths
    let includePaths =
        match dep.IncludeDirs with
        | Some dirs -> dirs |> List.map (fun d -> Path.Combine(packageDir, d))
        | None -> 
            if isFlappy then
                let distInc = Path.Combine(packageDir, "dist", "include")
                let inc = Path.Combine(packageDir, "include")
                if Directory.Exists distInc then [ distInc ]
                elif Directory.Exists inc then [ inc ]
                else [ packageDir ]
            else
                let inc = Path.Combine(packageDir, "include")
                if Directory.Exists inc then [ inc ] else [ packageDir ]
                
    // 2. Resolve Library Files (Link-time)
    let libs =
        let explicitLibs = 
            match dep.Libs with
            | Some libFiles -> libFiles |> List.map (fun l -> Path.Combine(packageDir, l))
            | None -> []

        let scannedLibs =
            match dep.LibDirs with
            | Some dirs ->
                let libExts = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then [ "*.lib" ] else [ "*.a"; "*.so"; "*.dylib" ]
                dirs |> List.collect (fun d -> 
                    let dir = Path.Combine(packageDir, d)
                    scanFiles dir libExts)
            | None -> []

        if not explicitLibs.IsEmpty || not scannedLibs.IsEmpty then
            explicitLibs @ scannedLibs
        else
            let libExts = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then [ "*.lib" ] else [ "*.a"; "*.so"; "*.dylib" ]
            if isFlappy then
                let distLib = Path.Combine(packageDir, "dist", "lib")
                if Directory.Exists distLib then scanFiles distLib libExts
                else scanFiles packageDir libExts
            else
                scanFiles packageDir libExts

    // 3. Resolve Runtime Libraries (DLLs, Shared Objects)
    let runtimeLibs =
        let dllExts = [ "*.dll"; "*.so"; "*.dylib" ]
        if isFlappy then
            let distLib = Path.Combine(packageDir, "dist", "lib")
            let distBin = Path.Combine(packageDir, "dist", "bin")
            let found = scanFiles distLib dllExts @ scanFiles distBin dllExts
            if found.IsEmpty then scanFiles packageDir dllExts else found
        else
            scanFiles packageDir dllExts
                
    { Name = dep.Name; IncludePaths = includePaths; Libs = libs |> List.distinct; RuntimeLibs = runtimeLibs |> List.distinct; Resolved = resolved }

let getCacheKey (dep: Dependency) (profile: BuildProfile) (compiler: string) (arch: string) =
    let profileStr = match profile with Debug -> "debug" | Release -> "release"
    let safeCompiler = compiler.Replace("\\", "_").Replace("/", "_").Replace(":", "")
    let suffix = $"{profileStr}_{arch}_{safeCompiler}"
    match dep.Source with
    | Git(url, tag) ->
        let version = tag |> Option.defaultValue "HEAD"
        let hash = computeHash url
        $"{dep.Name}@{version}_{hash}_{suffix}"
    | Url url ->
        let hash = computeHash url
        $"{dep.Name}@url_{hash}_{suffix}"
    | Local _ -> ""

let installToCache (dep: Dependency) (cachePath: string) : Result<unit, string> =
    if Directory.Exists cachePath then
        Ok()
    else
        Console.WriteLine $"[Cache] Downloading {dep.Name}..."
        match dep.Source with
        | Git(url, tag) ->
            let cloneArgs = $"clone {url} \"{cachePath}\""
            match executeGit cloneArgs "" with
            | Ok() ->
                match tag with
                | Some t -> executeGit $"checkout {t}" cachePath
                | None -> Ok()
            | Error e -> Error e
        | Url url ->
            Directory.CreateDirectory cachePath |> ignore
            let fileName = 
                let f = Path.GetFileName url
                if String.IsNullOrEmpty f then dep.Name + ".h" else f
            let dest = Path.Combine(cachePath, fileName)
            match downloadFile url dest with
            | Ok() -> Ok()
            | Error e -> 
                Directory.Delete(cachePath, true)
                Error e
        | Local _ -> Ok()

// New public function
let fetch (dep: Dependency) (profile: BuildProfile) (compiler: string) (arch: string) : Result<string, string> =
    match dep.Source with
    | Local path -> 
        let fullPath = if Path.IsPathRooted path then path else Path.GetFullPath path
        if Directory.Exists fullPath then Ok fullPath
        else Error $"Local path not found: {fullPath}"
    | _ ->
        let cacheDir = getGlobalCacheDir ()
        let key = getCacheKey dep profile compiler arch
        let cachedPath = Path.Combine(cacheDir, key)
        match installToCache dep cachedPath with
        | Ok () -> Ok cachedPath
        | Error e -> Error e

let install (dep: Dependency) (profile: BuildProfile) (compiler: string) (arch: string) (prevMetas: DependencyMetadata list) : Result<DependencyMetadata, string> =
    match dep.Source with
    | Local path ->
        let fullPath = if Path.IsPathRooted path then path else Path.GetFullPath path
        if Directory.Exists fullPath then
            match buildDependency dep fullPath profile compiler prevMetas with
            | Error e -> Error e
            | Ok() -> Ok(resolveDependencyMetadata dep fullPath "local")
        else
            Error $"Local path not found: {fullPath}"
    | _ ->
        let cacheDir = getGlobalCacheDir ()
        let key = getCacheKey dep profile compiler arch
        let cachedPath = Path.Combine(cacheDir, key)
        
        match installToCache dep cachedPath with
        | Error e -> Error e
        | Ok() ->
            match buildDependency dep cachedPath profile compiler prevMetas with
            | Error e -> Error e
            | Ok() ->
                let localDir = getLocalPackagesDir ()
                // Link name needs to be unique too if we link multiple? 
                // Actually, 'packages/fmt' usually points to ONE version.
                // If we are building Debug, packages/fmt -> cache/fmt_debug.
                // If we are building Release, packages/fmt -> cache/fmt_release.
                // This means 'packages/' folder is ephemeral per build state?
                // OR we link to 'packages/fmt' and inside it has artifacts?
                // CURRENT LINK LOGIC: createLink cachedPath linkPath.
                // It replaces the link. This is fine for now as long as we build one profile at a time.
                let linkPath = Path.Combine(localDir, dep.Name)
                createLink cachedPath linkPath
                Ok(resolveDependencyMetadata dep linkPath "remote")

let cleanBuildArtifacts (path: string) =
    let dirs = [ "flappy_build"; "dist"; "obj"; "bin" ]
    for d in dirs do
        let fullPath = Path.Combine(path, d)
        if Directory.Exists fullPath then
            try Directory.Delete(fullPath, true) with _ -> ()

let update (dep: Dependency) (profile: BuildProfile) (compiler: string) (arch: string) : Result<unit, string> =
    match dep.Source with
    | Local _ -> 
        Log.info "Update" $"Skipping local dependency {dep.Name}"
        Ok()
    | Git(url, tag) ->
        let cacheDir = getGlobalCacheDir()
        let key = getCacheKey dep profile compiler arch
        let cachedPath = Path.Combine(cacheDir, key)
        
        if not (Directory.Exists cachedPath) then
            Error $"Dependency {dep.Name} is not installed for this profile. Run 'flappy sync' first."
        else
            Log.info "Updating" $"{dep.Name} from {url}"
            // 1. Fetch and Reset
            match executeGit "fetch --all" cachedPath with
            | Error e -> Error e
            | Ok() ->
                let target = tag |> Option.defaultValue "HEAD"
                match executeGit $"checkout {target}" cachedPath with
                | Error e -> Error e
                | Ok() ->
                    match executeGit "pull" cachedPath with | _ -> () // Pull might fail if detached HEAD, ignore
                    // 2. Clean build artifacts to force re-build
                    cleanBuildArtifacts cachedPath
                    // 3. Also clean local package link/dir if any
                    let localPkgDir = Path.Combine(getLocalPackagesDir(), dep.Name)
                    cleanBuildArtifacts localPkgDir
                    // 4. Remove state file to trigger rebuild
                    let stateFile = Path.Combine(cachedPath, ".flappy_build_state")
                    if File.Exists stateFile then File.Delete stateFile
                    Ok()
    | Url url ->
        // For direct URLs, we just re-download
        let cacheDir = getGlobalCacheDir()
        let key = getCacheKey dep profile compiler arch
        let cachedPath = Path.Combine(cacheDir, key)
        if Directory.Exists cachedPath then Directory.Delete(cachedPath, true)
        Log.info "Updating" $"{dep.Name} (Re-downloading)"
        Ok() // Next sync will re-download

let cleanCache () =
    let dir = getGlobalCacheDir ()
    if Directory.Exists dir then
        try
            Directory.Delete(dir, true)
            Console.WriteLine $"Cache cleared: {dir}"
            Ok()
        with ex ->
            Error $"Failed to clear cache: {ex.Message}"
    else
        Console.WriteLine "Cache is already empty."
        Ok()