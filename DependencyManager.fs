module Flappy.DependencyManager

open System
open System.IO
open System.Net.Http
open System.Diagnostics
open Flappy.Config

let getPackagesDir () =
    let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    let dir = Path.Combine(appData, "flappy", "packages")
    if not (Directory.Exists(dir)) then
        Directory.CreateDirectory(dir) |> ignore
    dir

let runGit (args: string) (workingDir: string) =
    let psi = ProcessStartInfo(FileName = "git", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
    if not (String.IsNullOrEmpty(workingDir)) then
        psi.WorkingDirectory <- workingDir
    
    use p = Process.Start(psi)
    p.WaitForExit()
    if p.ExitCode <> 0 then
        let err = p.StandardError.ReadToEnd()
        Error $"Git command failed: {err}"
    else
        Ok ()

let downloadFile (url: string) (destPath: string) =
    try
        use client = new HttpClient()
        let content = client.GetByteArrayAsync(url).Result
        File.WriteAllBytes(destPath, content)
        Ok ()
    with
    | ex -> Error ex.Message

let resolveIncludePath (packageDir: string) =
    let includeDir = Path.Combine(packageDir, "include")
    if Directory.Exists(includeDir) then includeDir else packageDir

let install (dep: Dependency) : Result<string, string> =
    let packagesDir = getPackagesDir()
    let packagePath = Path.Combine(packagesDir, dep.Name)
    
    match dep.Source with
    | Local path ->
        // For local path, just return the absolute path
        if Path.IsPathRooted(path) then 
            if Directory.Exists(path) then Ok (resolveIncludePath path)
            else Error $"Local path not found: {path}"
        else
            // Relative to CWD? Or Project Root? 
            // We assume Builder calls this, so it should be relative to project root usually.
            // But let's return full path.
            let fullPath = Path.GetFullPath(path)
            if Directory.Exists(fullPath) then Ok (resolveIncludePath fullPath)
            else Error $"Local path not found: {fullPath}"

    | Git (url, tag) ->
        if Directory.Exists(packagePath) then
            // Already installed. 
            // Ideally we should check if it needs update, but for now simple cache.
            // If tag is specified, we might want to checkout?
            Ok (resolveIncludePath packagePath)
        else
            Console.WriteLine($"Installing {dep.Name} from {url}...")
            match runGit $"clone {url} {dep.Name}" packagesDir with
            | Ok () ->
                match tag with
                | Some t -> 
                    Console.WriteLine($"Checking out tag {t}...")
                    match runGit $"checkout {t}" packagePath with
                    | Ok () -> Ok (resolveIncludePath packagePath)
                    | Error e -> Error e
                | None -> Ok (resolveIncludePath packagePath)
            | Error e -> Error e

    | Url url ->
        if Directory.Exists(packagePath) then
            Ok (resolveIncludePath packagePath)
        else
            Console.WriteLine($"Downloading {dep.Name} from {url}...")
            Directory.CreateDirectory(packagePath) |> ignore
            // Try to guess filename from URL, fallback to dep.Name + extension
            let fileName = Path.GetFileName(url)
            let fileName = if String.IsNullOrEmpty(fileName) then dep.Name + ".h" else fileName
            let dest = Path.Combine(packagePath, fileName)
            
            match downloadFile url dest with
            | Ok () -> Ok (resolveIncludePath packagePath)
            | Error e -> 
                Directory.Delete(packagePath, true)
                Error e
