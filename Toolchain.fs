module Flappy.Toolchain

open System
open System.Diagnostics
open System.IO
open Flappy.VsDevCmd

type CompilerType =
    | MSVC
    | GCC
    | Clang
    | Unknown

type ToolchainInfo = {
    Name: string
    Command: string
    Type: CompilerType
    IsAvailable: bool
}

let checkCommand (cmd: string) =
    try
        let psi = ProcessStartInfo(FileName = cmd, Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
        use p = Process.Start(psi)
        if isNull p then false
        else
            p.WaitForExit()
            p.ExitCode = 0
    with
    | _ -> false

let getAvailableToolchains () =
    let toolchains = ResizeArray<ToolchainInfo>()

    // Check MSVC
    match findVsInstallPath() with
    | Some _ -> 
        toolchains.Add({ Name = "Visual Studio (MSVC)"; Command = "cl"; Type = MSVC; IsAvailable = true })
    | None -> ()

    // Check g++
    if checkCommand "g++" then
        toolchains.Add({ Name = "GCC (g++)"; Command = "g++"; Type = GCC; IsAvailable = true })

    // Check clang++
    if checkCommand "clang++" then
        toolchains.Add({ Name = "Clang (clang++)"; Command = "clang++"; Type = Clang; IsAvailable = true })

    toolchains |> Seq.toList