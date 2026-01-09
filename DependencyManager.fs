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

let executeCommand (cmd: string) (args: string) (workingDir: string) : Result<unit, string> =
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    if not (String.IsNullOrEmpty workingDir) then
        psi.WorkingDirectory <- workingDir
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
    executeCommand "git" args workingDir

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

let buildDependency (dep: Dependency) (path: string) (profile: BuildProfile) (compiler: string) =
    let profileStr = match profile with Debug -> "Debug" | Release -> "Release"
    match dep.BuildCmd with
    | Some cmd ->
        Log.info "Build" $"Custom command for {dep.Name}"
        let finalCmd, finalArgs = 
            match patchCommandForMsvc "cmd.exe" $"/c {cmd}" "x64" with
            | Some(c, a) -> c, a
            | None -> "cmd.exe", $"/c {cmd}"
        executeCommand finalCmd finalArgs path
    | None ->
        if File.Exists(Path.Combine(path, "flappy.toml")) then
            Log.info "Build" $"Flappy build for {dep.Name}"
            let exePath = Process.GetCurrentProcess().MainModule.FileName
            let args = if profile = Release then "build --release" else "build"
            // Note: We currently don't propagate compiler to sub-flappy builds via CLI args easily 
            // unless we add --compiler flag to build command or rely on flappy.toml cascading.
            // For now, sub-flappy will use its own resolution.
            executeCommand exePath args path
        elif File.Exists(Path.Combine(path, "CMakeLists.txt")) then
            let buildDir = Path.Combine(path, "flappy_build")
            if not (Directory.Exists buildDir) then
                Directory.CreateDirectory buildDir |> ignore
            Log.info "Configure" $"{dep.Name} (CMake)"
            
            // Heuristic: If compiler looks like a C++ compiler, pass it as CMAKE_CXX_COMPILER
            // If it's cl.exe, CMake handles it via generator usually, but passing it is safe.
            // We quote it to handle paths with spaces.
            let cmakeArgs = $"-S \"{path}\" -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={profileStr} -DCMAKE_CXX_COMPILER=\"{compiler}\""
            
            match executeCommand "cmake" cmakeArgs path with
            | Error e -> Error e
            | Ok() -> 
                Log.info "Compile" $"{dep.Name} (CMake)"
                executeCommand "cmake" $"--build \"{buildDir}\" --config {profileStr}" path
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
        match dep.Libs with
        | Some libFiles -> libFiles |> List.map (fun l -> Path.Combine(packageDir, l))
        | None -> 
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
                
    { IncludePaths = includePaths; Libs = libs |> List.distinct; RuntimeLibs = runtimeLibs |> List.distinct; Resolved = resolved }

let getCacheKey (dep: Dependency) =
    match dep.Source with
    | Git(url, tag) ->
        let version = tag |> Option.defaultValue "HEAD"
        let hash = computeHash url
        $"{dep.Name}@{version}_{hash}"
    | Url url ->
        let hash = computeHash url
        $"{dep.Name}@url_{hash}"
    | Local _ -> ""

let installToCache (dep: Dependency) (cachePath: string) (profile: BuildProfile) (compiler: string) : Result<unit, string> =
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

let install (dep: Dependency) (profile: BuildProfile) (compiler: string) : Result<DependencyMetadata, string> =
    match dep.Source with
    | Local path ->
        let fullPath = if Path.IsPathRooted path then path else Path.GetFullPath path
        if Directory.Exists fullPath then
            match buildDependency dep fullPath profile compiler with
            | Error e -> Error e
            | Ok() -> Ok(resolveDependencyMetadata dep fullPath "local")
        else
            Error $"Local path not found: {fullPath}"
    | _ ->
        let cacheDir = getGlobalCacheDir ()
        let key = getCacheKey dep
        let cachedPath = Path.Combine(cacheDir, key)
        // Note: installToCache doesn't build, it just downloads. 
        // Wait, the previous implementation built INSIDE installToCache?
        // Let's check the old code.
        // Old code: match installToCache... -> match buildDependency...
        // Ah, buildDependency is called AFTER installToCache.
        // So installToCache signature doesn't strictly need compiler if it just downloads.
        // But wait, my new signature for installToCache included it.
        // Let's re-read the old code logic.
        
        match installToCache dep cachedPath profile compiler with
        | Error e -> Error e
        | Ok() ->
            match buildDependency dep cachedPath profile compiler with
            | Error e -> Error e
            | Ok() ->
                let localDir = getLocalPackagesDir ()
                let linkPath = Path.Combine(localDir, dep.Name)
                createLink cachedPath linkPath
                Ok(resolveDependencyMetadata dep linkPath "remote")

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