module Flappy.Toolchain

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text.RegularExpressions
open Flappy.Config

type CompilerType =
    | MSVC
    | GCC
    | Clang
    | Unknown

type ToolchainInfo =
    {
        Name: string
        Command: string
        Type: CompilerType
        IsAvailable: bool
    }

let checkCommand (cmd: string) =
    if String.IsNullOrWhiteSpace cmd then
        false
    else
        try
            let args = if cmd.ToLower().Contains("cl") then "" else "--version"
            let psi =
                ProcessStartInfo(
                    FileName = cmd,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                )
            use p = Process.Start psi
            if isNull p then false
            else
                p.WaitForExit()
                true
        with _ ->
            false

let getCompilerVersion (cmd: string) =
    try
        let psi =
            ProcessStartInfo(
                FileName = cmd,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            )
        use p = Process.Start psi
        if isNull p then "Unknown"
        else
            let line = p.StandardOutput.ReadLine()
            p.WaitForExit()
            if String.IsNullOrWhiteSpace line then "Unknown" else line.Split(' ').[0..2] |> String.concat " "
    with _ -> 
        // Fallback for cl.exe which doesn't support --version
        if cmd.ToLower().EndsWith("cl.exe") || cmd.ToLower() = "cl" then "MSVC"
        else "Unknown"

let findClExe vsPath =
    let msvcBase = Path.Combine(vsPath, "VC", "Tools", "MSVC")
    if Directory.Exists msvcBase then
        let versions = Directory.GetDirectories(msvcBase) |> Array.toList |> List.sortDescending
        match versions |> List.tryHead with
        | Some v -> 
            let clPath = Path.Combine(v, "bin", "Hostx64", "x64", "cl.exe")
            if File.Exists clPath then Some clPath else None
        | None -> None
    else None

let getAvailableToolchains () =
    let toolchains = ResizeArray<ToolchainInfo>()

    // 1. Check MSVC (Windows)
    let vsInstalls = getVsInstallations()
    if not vsInstalls.IsEmpty then
        // Add a generic auto-detect option first for better portability
        toolchains.Add
            {
                Name = "Visual Studio (Auto-detect Latest)"
                Command = "cl"
                Type = MSVC
                IsAvailable = true
            }

    for vs in vsInstalls do
        let clPath = findClExe vs.Path |> Option.defaultValue vs.Path
        toolchains.Add
            {
                Name = $"{vs.Name} ({vs.Version})"
                Command = clPath
                Type = MSVC
                IsAvailable = true
            }
        
        // Also check for vs-clang
        let vsClangPath = Path.Combine(vs.Path, "VC", "Tools", "Llvm", "x64", "bin", "clang-cl.exe")
        if File.Exists vsClangPath then
            toolchains.Add
                {
                    Name = $"Clang-CL in {vs.Name}"
                    Command = vsClangPath
                    Type = Clang
                    IsAvailable = true
                }

    // 2. Check GCC and versioned g++
    let prefixes = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then ["g++"] else ["g++"; "g++-"]
    // Simple path search for Unix-like systems
    let searchBinaries prefix =
        let paths = 
            if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then [] 
            else ["/usr/bin"; "/usr/local/bin"; "/home/sean/.nix-profile/bin"]
        paths 
        |> List.filter Directory.Exists
        |> List.collect (fun p -> Directory.GetFiles(p, prefix + "*") |> Array.toList)
        |> List.filter (fun f -> Regex.IsMatch(Path.GetFileName f, @"^g\+\+(-\d+)?$|^clang\+\+(-\d+)?$"))

    let unixCompilers = (searchBinaries "g++") @ (searchBinaries "clang++")
    
    for comp in unixCompilers do
        let name = Path.GetFileName comp
        let isGcc = name.Contains("g++")
        let typeName = if isGcc then "GCC" else "Clang"
        let version = getCompilerVersion comp
        toolchains.Add {
            Name = $"{typeName} ({version})"
            Command = comp
            Type = if isGcc then GCC else Clang
            IsAvailable = true
        }

    // 3. Fallback to PATH if nothing found or on Windows for MinGW
    if toolchains.Count = 0 || RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        let pathCompilers = ["g++"; "clang++"]
        for cmd in pathCompilers do
            if checkCommand cmd then
                let isGcc = cmd.Contains("g++")
                let typeName = if isGcc then "GCC" else "Clang"
                toolchains.Add {
                    Name = $"{typeName} (from PATH)"
                    Command = cmd
                    Type = if isGcc then GCC else Clang
                    IsAvailable = true
                }

    toolchains |> Seq.toList |> List.distinctBy (fun t -> t.Name + t.Command)
