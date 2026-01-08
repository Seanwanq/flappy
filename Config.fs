module Flappy.Config

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open System.Text.RegularExpressions
open Tomlyn
open Tomlyn.Model

type PackageConfig = 
    { Name: string
      Version: string
      Authors: string list }

type BuildConfig =

    {

        Compiler: string

        Language: string

        Standard: string

        Output: string

        Arch: string

        Type: string

        Defines: string list

        Flags: string list

    }



type DependencySource = 
    | Git of url: string * tag: string option
    | Url of url: string
    | Local of path: string

type Dependency = 
    { Name: string
      Source: DependencySource
      Defines: string list
      BuildCmd: string option
      IncludeDirs: string list option
      Libs: string list option }

type DependencyMetadata = 
    { IncludePaths: string list
      Libs: string list
      Resolved: string }

type TestConfig = 
    { Sources: string list
      Output: string
      Defines: string list }

type FlappyConfig = 
    { Package: PackageConfig
      Build: BuildConfig
      Test: TestConfig option
      Dependencies: Dependency list }

type LockEntry = 
    { Name: string
      Source: string
      Resolved: string }

type LockConfig = { Entries: LockEntry list }

module Log = 
    let info (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Green
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)

    let warn (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Yellow
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)

    let error (action: string) (message: string) =
        Console.ForegroundColor <- ConsoleColor.Red
        let paddedAction = action.PadLeft(12)
        Console.Write($"{paddedAction} ")
        Console.ResetColor()
        Console.WriteLine(message)

type BuildProfile = Debug | Release

let defaultConfig =
    {
        Package =
            {
                Name = "untitled"
                Version = "0.1.0"
                Authors = []
            }
        Build =
            {
                Compiler = "g++"
                Language = "c++"
                Standard = "c++17"
                Output = "main"
                Arch = "x64"
                Type = "exe"
                Defines = []
                Flags = []
            }
        Test = None
        Dependencies = []
    }

let parse (tomlContent: string) (profileOverride: string option) : Result<FlappyConfig, string> =
    try
        let model = Toml.ToModel tomlContent

        let getTable (key: string) (m: TomlTable) =
            if m.ContainsKey(key) && m.[key] :? TomlTable then
                Some(m.[key] :?> TomlTable)
            else
                None

        let getString (key: string) (m: TomlTable) (def: string) =
            if m.ContainsKey(key) && m.[key] :? string then
                m.[key] :?> string
            else
                def

        let getOptString (key: string) (m: TomlTable) =
            if m.ContainsKey key && m.[key] :? string then
                Some(m.[key] :?> string)
            else
                None

        let getList (key: string) (m: TomlTable) =
            if m.ContainsKey key && m.[key] :? TomlArray then
                (m.[key] :?> TomlArray) |> Seq.cast<obj> |> Seq.map string |> Seq.toList
            else
                []

        let packageConfig =
            match getTable "package" model with
            | Some pkg ->
                {
                    Name = getString "name" pkg defaultConfig.Package.Name
                    Version = getString "version" pkg defaultConfig.Package.Version
                    Authors = getList "authors" pkg
                }
            | None -> defaultConfig.Package

        let merge (baseCfg: BuildConfig) (overrides: TomlTable) =
            { Compiler = getString "compiler" overrides baseCfg.Compiler
              Language = getString "language" overrides baseCfg.Language
              Standard = getString "standard" overrides baseCfg.Standard
              Output = getString "output" overrides baseCfg.Output
              Arch = getString "arch" overrides baseCfg.Arch
              Type = getString "type" overrides baseCfg.Type
              Defines = baseCfg.Defines @ (getList "defines" overrides)
              Flags = baseCfg.Flags @ (getList "flags" overrides) }

        let buildConfig =
            match getTable "build" model with
            | Some build ->
                // 1. Get base values
                let baseCfg = 
                    { defaultConfig.Build with
                        Compiler = getString "compiler" build defaultConfig.Build.Compiler
                        Language = getString "language" build defaultConfig.Build.Language
                        Standard = getString "standard" build defaultConfig.Build.Standard
                        Output = getString "output" build defaultConfig.Build.Output
                        Arch = getString "arch" build defaultConfig.Build.Arch
                        Type = getString "type" build defaultConfig.Build.Type
                        Defines = getList "defines" build
                        Flags = getList "flags" build }

                let platformKey = 
                    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
                    elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
                    elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "macos"
                    else "unknown"

                match profileOverride with
                | Some profile ->
                    // Flow: Base -> Profile -> Profile.Platform
                    match getTable profile build with
                    | Some profTable ->
                        let profCfg = merge baseCfg profTable
                        match getTable platformKey profTable with
                        | Some profPlatTable -> merge profCfg profPlatTable
                        | None -> profCfg
                    | None -> baseCfg // Profile not found, fallback to base? Or error? Fallback for now.
                | None ->
                    // Flow: Base -> Platform
                    match getTable platformKey build with
                    | Some platTable -> merge baseCfg platTable
                    | None -> baseCfg
            | None -> { defaultConfig.Build with Defines = []; Flags = [] }

        let testConfig =
            match getTable "test" model with
            | Some test ->
                Some {
                    Sources = getList "sources" test
                    Output = getString "output" test "bin/test_runner"
                    Defines = getList "defines" test
                }
            | None -> None

        let dependencies =
            match getTable "dependencies" model with
            | Some deps ->
                deps.Keys
                |> Seq.choose (fun key ->
                    if deps.[key] :? TomlTable then
                        let depTable = deps.[key] :?> TomlTable
                        let source =
                            if depTable.ContainsKey("git") then
                                Some(Git(getString "git" depTable "", getOptString "tag" depTable))
                            elif depTable.ContainsKey("url") then
                                Some(Url(getString "url" depTable ""))
                            elif depTable.ContainsKey("path") then
                                Some(Local(getString "path" depTable ""))
                            else None
                        let depDefines = getList "defines" depTable
                        let buildCmd = getOptString "build_cmd" depTable
                        let includeDirs = if depTable.ContainsKey("include_dirs") then Some (getList "include_dirs" depTable) else None
                        let libs = if depTable.ContainsKey("libs") then Some (getList "libs" depTable) else None
                        match source with
                        | Some s -> Some { Name = key; Source = s; Defines = depDefines; BuildCmd = buildCmd; IncludeDirs = includeDirs; Libs = libs }
                        | None -> None
                    else None)
                |> Seq.toList
            | None -> []

        Ok { Package = packageConfig; Build = buildConfig; Test = testConfig; Dependencies = dependencies }
    with ex -> Error ex.Message

let updatePlatformConfig (filePath: string) (platform: string) (compiler: string) (std: string) : Result<unit, string> =
    try
        if not (File.Exists filePath) then Error "flappy.toml not found."
        else
            let content = File.ReadAllText filePath
            let model = Toml.ToModel content
            
            // Get or create the build table
            let buildTable = 
                if model.ContainsKey("build") && model.["build"] :? TomlTable then
                    model.["build"] :?> TomlTable
                else
                    let t = TomlTable()
                    model.["build"] <- t
                    t
            
            // Get or create the platform sub-table
            let platformTable = 
                if buildTable.ContainsKey(platform) && buildTable.[platform] :? TomlTable then
                    buildTable.[platform] :?> TomlTable
                else
                    let t = TomlTable()
                    buildTable.[platform] <- t
                    t
            
            platformTable.["compiler"] <- compiler
            platformTable.["standard"] <- std
            
            let newContent = Toml.FromModel(model)
            let prettyContent = Regex.Replace(newContent, @"\n(\[build\.)", "\n\n$1")
            File.WriteAllText(filePath, prettyContent)
            Ok ()
    with ex -> Error ex.Message

let parseLock (tomlContent: string) : LockConfig =
    try
        let model = Toml.ToModel tomlContent
        let entries =
            if model.ContainsKey "dependencies" && model.["dependencies"] :? TomlArray then
                (model.["dependencies"] :?> TomlArray)
                |> Seq.cast<TomlTable>
                |> Seq.map (fun t ->
                    { Name = t.["name"] :?> string
                      Source = t.["source"] :?> string
                      Resolved = t.["resolved"] :?> string })
                |> Seq.toList
            else []
        { Entries = entries }
    with _ -> { Entries = [] }

let saveLock (filePath: string) (lock: LockConfig) =
    let sb = System.Text.StringBuilder()
    sb.AppendLine "# This file is automatically generated by flappy." |> ignore
    sb.AppendLine "# It is used to lock dependencies to specific versions." |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine "[[dependencies]]" |> ignore
    lock.Entries
    |> List.iteri (fun i e ->
        if i > 0 then sb.AppendLine "\n[[dependencies]]" |> ignore
        sb.AppendLine $"name = \"{e.Name}\" " |> ignore
        sb.AppendLine $"source = \"{e.Source}\" " |> ignore
        sb.AppendLine $"resolved = \"{e.Resolved}\" " |> ignore) 
    File.WriteAllText(filePath, sb.ToString())

let addDependency (filePath: string) (name: string) (tomlLine: string) : Result<unit, string> =
    try
        if not (File.Exists filePath) then Error "flappy.toml not found."
        else
            let lines = File.ReadAllLines filePath |> Array.toList
            let regex = Regex($"^\s*{Regex.Escape name}\s*=", RegexOptions.IgnoreCase)
            if lines |> List.exists (fun l -> regex.IsMatch l) then
                Error $"Dependency '{name}' already exists in flappy.toml."
            else
                match lines |> List.tryFindIndex (fun l -> l.Trim() = "[dependencies]") with
                | Some idx ->
                    let newLines = lines.[0..idx] @ [ tomlLine ] @ lines.[idx + 1 ..]
                    File.WriteAllLines(filePath, newLines)
                    Ok()
                | None ->
                    let newLines = lines @ [ ""; ""; "[dependencies]"; tomlLine ]
                    File.WriteAllLines(filePath, newLines)
                    Ok()
    with ex -> Error ex.Message

let removeDependency (filePath: string) (name: string) : Result<unit, string> =
    try
        if not (File.Exists filePath) then Error "flappy.toml not found."
        else
            let lines = File.ReadAllLines filePath |> Array.toList
            let regex = Regex($"^\s*{Regex.Escape name}\s*=", RegexOptions.IgnoreCase)
            let newLines = lines |> List.filter (fun l -> not (regex.IsMatch l))
            if newLines.Length = lines.Length then
                Error $"Dependency '{name}' not found in flappy.toml."
            else
                File.WriteAllLines(filePath, newLines)
                Ok()
    with ex -> Error ex.Message

let findVsInstallPath () =
    let vswherePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe")
    if File.Exists vswherePath then
        let psi = ProcessStartInfo(FileName = vswherePath, Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true)
        use p = Process.Start(psi)
        let output = p.StandardOutput.ReadToEnd().Trim()
        p.WaitForExit()
        if p.ExitCode = 0 && not (String.IsNullOrWhiteSpace output) then Some output else None
    else None

let getVcVarsAllPath (vsPath: string) =
    let path = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvarsall.bat")
    if File.Exists path then Some path else None

let patchCommandForMsvc (compiler: string) (args: string) (arch: string) : (string * string) option =
    let c = compiler.ToLower()
    let msvcCommands = ["cl"; "cl.exe"; "msvc"; "clang-cl"; "clang-cl.exe"; "lib"; "lib.exe"]
    if List.contains c msvcCommands || (c = "cmd.exe" && args.Contains("cl ")) then
        match findVsInstallPath() with
        | Some vsPath ->
            match getVcVarsAllPath vsPath with
            | Some vcvarsPath -> Some ("cmd.exe", $"/c \"call \"{vcvarsPath}\" {arch} && {compiler} {args}\" ")
            | None -> None
        | None -> None
    else None