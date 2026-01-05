module Flappy.VsDevCmd

open System
open System.IO
open System.Diagnostics

let findVsInstallPath () =
    let vswherePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe")
    if File.Exists vswherePath then
        let psi = ProcessStartInfo(FileName = vswherePath, Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true)
        use p = Process.Start(psi)
        let output = p.StandardOutput.ReadToEnd().Trim()
        p.WaitForExit()
        if p.ExitCode = 0 && not (String.IsNullOrWhiteSpace output) then
            Some output
        else
            None
    else
        None

let getVcVarsAllPath (vsPath: string) =
    let path = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvarsall.bat")
    if File.Exists path then Some path else None

let patchCommandForMsvc (compiler: string) (args: string) : (string * string) option =
    if compiler.ToLower() = "cl" || compiler.ToLower() = "cl.exe" || compiler.ToLower() = "msvc" then
        match findVsInstallPath() with
        | Some vsPath ->
            match getVcVarsAllPath vsPath with
            | Some vcvarsPath ->
                // Construct a command that runs vcvarsall.bat then the compiler
                // We use cmd /c "call ... && cl ..."
                let newCmd = "cmd.exe"
                // Using triple quotes to safely embed double quotes
                let newArgs = $"""/c "call "{vcvarsPath}" x64 && {compiler} {args}" """
                Some (newCmd, newArgs)
            | None -> 
                printfn "Warning: Found VS at %s but vcvarsall.bat is missing." vsPath
                None
        | None -> 
            printfn "Warning: MSVC compiler specified but Visual Studio installation not found via vswhere."
            None
    else
        None