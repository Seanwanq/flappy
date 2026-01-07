module Flappy.DependencyManager

open System
open System.IO
open System.Net.Http
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Runtime.InteropServices
open Flappy.Config

// Global Cache
let getGlobalCacheDir () =
    let basePath =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        else
            let xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")

            if not (String.IsNullOrWhiteSpace(xdg)) then
                xdg
            else
                let home = Environment.GetEnvironmentVariable("HOME")

                if not (String.IsNullOrWhiteSpace(home)) then
                    Path.Combine(home, ".cache")
                else
                    Environment.GetFolderPath Environment.SpecialFolder.ApplicationData

    let dir = Path.Combine(basePath, "flappy", "cache")

    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore

    dir

// Local Project Packages
let getLocalPackagesDir () =
    let dir = "packages"

    if not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore

    dir

let computeHash (input: string) =
    use sha = SHA256.Create()
    let bytes = Encoding.UTF8.GetBytes input
    let hash = sha.ComputeHash bytes
    BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 12)

let executeGit (args: string) (workingDir: string) : Result<unit, string> =
    let psi =
        ProcessStartInfo(
            FileName = "git",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        )

    if not (String.IsNullOrEmpty workingDir) then
        psi.WorkingDirectory <- workingDir

    use p = Process.Start psi

    if isNull p then
        Error "Failed to start git process"
    else
        p.WaitForExit()

        if p.ExitCode <> 0 then
            let err = p.StandardError.ReadToEnd()
            Error $"Git command failed: {err}"
        else
            Ok()

let getGitCommit (workingDir: string) : string =
    try
        let psi =
            ProcessStartInfo(
                FileName = "git",
                Arguments = "rev-parse HEAD",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            )

        psi.WorkingDirectory <- workingDir
        use p = Process.Start psi

        if isNull p then
            "unknown"
        else
            let output = p.StandardOutput.ReadToEnd().Trim()
            p.WaitForExit()
            if p.ExitCode = 0 then output else "unknown"
    with _ ->
        "unknown"

let runCmd (args: string) =
    let psi =
        ProcessStartInfo(
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        )

    use p = Process.Start psi

    if not (isNull p) then
        p.WaitForExit()
        p.ExitCode = 0
    else
        false

let createLink (target: string) (link: string) =
    if Directory.Exists link || File.Exists link then
        try
            Directory.Delete(link, false)
        with _ ->
            ()

    if not (Directory.Exists link) then
        try
            Directory.CreateSymbolicLink(link, target) |> ignore
        with _ ->
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let args = $"/c mklink /J \"{link}\" \"{target}\""

                if not (runCmd args) then
                    Console.WriteLine $"Warning: Failed to link {link} -> {target}. Please check permissions."
            else
                // Unix fallback: ln -s
                let psi =
                    ProcessStartInfo(
                        FileName = "ln",
                        Arguments = $"-s \"{target}\" \"{link}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    )

                use p = Process.Start psi
                p.WaitForExit()

                if p.ExitCode <> 0 then
                    Console.WriteLine $"Warning: Failed to link {link} -> {target}. ln -s exit code: {p.ExitCode}"

let downloadFile (url: string) (destPath: string) =
    try
        use client = new HttpClient()
        let content = client.GetByteArrayAsync(url).Result
        File.WriteAllBytes(destPath, content)
        Ok()
    with ex ->
        Error ex.Message

let resolveIncludePath (packageDir: string) =
    let includeDir = Path.Combine(packageDir, "include")

    if Directory.Exists includeDir then
        includeDir
    else
        packageDir

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
                | Some t ->
                    let checkoutArgs = $"checkout {t}"
                    executeGit checkoutArgs cachePath
                | None -> Ok()
            | Error e -> Error e

        | Url url ->
            Directory.CreateDirectory cachePath |> ignore
            let fileName = Path.GetFileName url

            let fileName =
                if String.IsNullOrEmpty fileName then
                    dep.Name + ".h"
                else
                    fileName

            let dest = Path.Combine(cachePath, fileName)

            match downloadFile url dest with
            | Ok() -> Ok()
            | Error e ->
                Directory.Delete(cachePath, true)
                Error e

        | Local _ -> Ok()

let install (dep: Dependency) : Result<string * string, string> =
    match dep.Source with
    | Local path ->
        let fullPath =
            if Path.IsPathRooted path then
                path
            else
                Path.GetFullPath path

        if Directory.Exists fullPath then
            Ok(resolveIncludePath fullPath, "local")
        else
            Error $"Local path not found: {fullPath}"

    | _ ->
        let cacheDir = getGlobalCacheDir ()
        let key = getCacheKey dep
        let cachedPath = Path.Combine(cacheDir, key)

        match installToCache dep cachedPath with
        | Error e -> Error e
        | Ok() ->
            let localDir = getLocalPackagesDir ()
            let linkPath = Path.Combine(localDir, dep.Name)

            createLink cachedPath linkPath

            let resolved =
                match dep.Source with
                | Git _ -> getGitCommit cachedPath
                | Url url -> computeHash url
                | Local _ -> "local"

            Ok(resolveIncludePath linkPath, resolved)

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
