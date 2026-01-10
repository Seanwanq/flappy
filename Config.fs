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
      LibDirs: string list option
      Libs: string list option
      ExtraDependencies: string list }

type DependencyMetadata = 
    { Name: string
      IncludePaths: string list
      Libs: string list
      RuntimeLibs: string list
      Resolved: string }

type TestConfig = 
    { Sources: string list
      Output: string
      Defines: string list
      Flags: string list }

type FlappyConfig = 
    { Package: PackageConfig
      Build: BuildConfig
      Test: TestConfig option
      Dependencies: Dependency list
      IsProfileDefined: bool }

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
        IsProfileDefined = false
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

        let mutable wasProfileDefined = false
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
                        wasProfileDefined <- true
                        let profCfg = merge baseCfg profTable
                        match getTable platformKey profTable with
                        | Some profPlatTable -> merge profCfg profPlatTable
                        | None -> profCfg
                    | None -> 
                        wasProfileDefined <- false
                        baseCfg
                | None ->
                    // Flow: Base -> Platform
                    match getTable platformKey build with
                    | Some platTable -> 
                        wasProfileDefined <- true
                        merge baseCfg platTable
                    | None -> 
                        wasProfileDefined <- false
                        baseCfg
            | None -> { defaultConfig.Build with Defines = []; Flags = [] }

        let testConfig =
            match getTable "test" model with
            | Some test ->
                Some {
                    Sources = getList "sources" test
                    Output = getString "output" test "bin/test_runner"
                    Defines = getList "defines" test
                    Flags = getList "flags" test
                }
            | None -> None

        let dependencies =
            match getTable "dependencies" model with
            | Some deps ->
                let platformKey = 
                    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
                    elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
                    elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "macos"
                    else "unknown"

                deps.Keys
                |> Seq.choose (fun key ->
                    if deps.[key] :? TomlTable then
                        let depTable = deps.[key] :?> TomlTable
                        
                        // 1. Parse Base Configuration
                        let source =
                            if depTable.ContainsKey("git") then
                                Some(Git(getString "git" depTable "", getOptString "tag" depTable))
                            elif depTable.ContainsKey("url") then
                                Some(Url(getString "url" depTable ""))
                            elif depTable.ContainsKey("path") then
                                Some(Local(getString "path" depTable ""))
                            else None
                            
                        let baseDefines = getList "defines" depTable
                        let baseBuildCmd = getOptString "build_cmd" depTable
                        let baseIncludeDirs = if depTable.ContainsKey("include_dirs") then Some (getList "include_dirs" depTable) else None
                        let baseLibDirs = if depTable.ContainsKey("lib_dirs") then Some (getList "lib_dirs" depTable) else None
                        let baseLibs = if depTable.ContainsKey("libs") then Some (getList "libs" depTable) else None
                        let baseExtraDeps = getList "dependencies" depTable

                        // 2. Parse Platform Overrides
                        let finalDefines, finalBuildCmd, finalIncludeDirs, finalLibDirs, finalLibs, finalExtraDeps =
                            if depTable.ContainsKey(platformKey) && depTable.[platformKey] :? TomlTable then
                                let platTable = depTable.[platformKey] :?> TomlTable
                                let platDefines = getList "defines" platTable
                                let platBuildCmd = getOptString "build_cmd" platTable
                                let platIncludeDirs = if platTable.ContainsKey("include_dirs") then Some (getList "include_dirs" platTable) else None
                                let platLibDirs = if platTable.ContainsKey("lib_dirs") then Some (getList "lib_dirs" platTable) else None
                                let platLibs = if platTable.ContainsKey("libs") then Some (getList "libs" platTable) else None
                                let platExtraDeps = getList "dependencies" platTable
                                
                                // Merge logic:
                                // Defines: Append
                                // BuildCmd: Override if present
                                // IncludeDirs: Override if present
                                // LibDirs: Override if present
                                // Libs: Override if present
                                // ExtraDependencies: Append
                                (baseDefines @ platDefines,
                                 (if platBuildCmd.IsSome then platBuildCmd else baseBuildCmd),
                                 (if platIncludeDirs.IsSome then platIncludeDirs else baseIncludeDirs),
                                 (if platLibDirs.IsSome then platLibDirs else baseLibDirs),
                                 (if platLibs.IsSome then platLibs else baseLibs),
                                 baseExtraDeps @ platExtraDeps)
                            else
                                (baseDefines, baseBuildCmd, baseIncludeDirs, baseLibDirs, baseLibs, baseExtraDeps)

                        match source with
                        | Some s -> Some { Name = key; Source = s; Defines = finalDefines; BuildCmd = finalBuildCmd; IncludeDirs = finalIncludeDirs; LibDirs = finalLibDirs; Libs = finalLibs; ExtraDependencies = finalExtraDeps }
                        | None -> None
                    else None)
                |> Seq.toList
            | None -> []

        Ok { Package = packageConfig; Build = buildConfig; Test = testConfig; Dependencies = dependencies; IsProfileDefined = wasProfileDefined }
    with ex -> Error ex.Message

let formatToml (content: string) =
    // Ensure exactly one blank line before any section header [section]
    // 1. Normalize all line endings to LF and collapse multiple empty lines
    let normalized = content.Replace("\r\n", "\n")
    let collapsed = Regex.Replace(normalized, @"\n{2,}", "\n")
    // 2. Insert a blank line before every section header except the first one
    let formatted = Regex.Replace(collapsed, @"\n(\[)", "\n\n$1")
    formatted.Trim() + "\n"

let updateProfileConfig (filePath: string) (profile: string) (platform: string option) (compiler: string) : Result<unit, string> =
    try
        if not (File.Exists filePath) then Error "flappy.toml not found."
        else
            let content = File.ReadAllText filePath
            let model = Toml.ToModel content
            
            // 1. Get or create the 'build' table
            let buildTable = 
                if model.ContainsKey("build") && model.["build"] :? TomlTable then
                    model.["build"] :?> TomlTable
                else
                    let t = TomlTable()
                    model.["build"] <- t
                    t
            
            // 2. Get or create the profile table (e.g. [build.myprofile] or [build.windows])
            let profileTable =
                if buildTable.ContainsKey(profile) && buildTable.[profile] :? TomlTable then
                    buildTable.[profile] :?> TomlTable
                else
                    let t = TomlTable()
                    buildTable.[profile] <- t
                    t
            
            match platform with
            | Some plat ->
                // 3. Get or create the platform sub-table (e.g. [build.myprofile.windows])
                let platformTable =
                    if profileTable.ContainsKey(plat) && profileTable.[plat] :? TomlTable then
                        profileTable.[plat] :?> TomlTable
                    else
                        let t = TomlTable()
                        profileTable.[plat] <- t
                        t
                platformTable.["compiler"] <- compiler
            | None ->
                // No platform, set compiler directly in profile
                profileTable.["compiler"] <- compiler
            
            let newContent = Toml.FromModel(model)
            File.WriteAllText(filePath, formatToml newContent)
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
                let newContent = 
                    match lines |> List.tryFindIndex (fun l -> l.Trim() = "[dependencies]") with
                    | Some idx ->
                        let updated = lines.[0..idx] @ [ tomlLine ] @ lines.[idx + 1 ..]
                        String.concat "\n" updated
                    | None ->
                        let updated = lines @ [ "[dependencies]"; tomlLine ]
                        String.concat "\n" updated
                File.WriteAllText(filePath, formatToml newContent)
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

type VsInstallation = {
    Name: string
    Path: string
    Version: string
}

let getVsInstallations () : VsInstallation list =
    let vswherePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe")
    if File.Exists vswherePath then
        let psi = ProcessStartInfo(FileName = vswherePath, Arguments = "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true)
        try
            use p = Process.Start(psi)
            let output = p.StandardOutput.ReadToEnd()
            p.WaitForExit()
            if p.ExitCode = 0 then
                let nameRegex = Regex(@"""displayName"":\s*""(?<val>.*?)""")
                let pathRegex = Regex(@"""installationPath"":\s*""(?<val>.*?)""")
                let versionRegex = Regex(@"""productDisplayVersion"":\s*""(?<val>.*?)""")
                
                let names = nameRegex.Matches(output) |> Seq.cast<Match> |> Seq.map (fun m -> m.Groups.["val"].Value) |> Seq.toList
                let paths = pathRegex.Matches(output) |> Seq.cast<Match> |> Seq.map (fun m -> m.Groups.["val"].Value.Replace("\\\\", "\\")) |> Seq.toList
                let versions = versionRegex.Matches(output) |> Seq.cast<Match> |> Seq.map (fun m -> m.Groups.["val"].Value) |> Seq.toList
                
                List.zip3 names paths versions 
                |> List.map (fun (n, p, v) -> { Name = n; Path = p; Version = v })
            else []
        with _ -> []
    else []

let findVsInstallPath () =
    getVsInstallations () |> List.tryHead |> Option.map (fun x -> x.Path)

let getVcVarsAllPath (vsPath: string) =
    let path = Path.Combine(vsPath, "VC", "Auxiliary", "Build", "vcvarsall.bat")
    if File.Exists path then Some path else None

let findProjectRoot (startDir: string) =
    let mutable current = DirectoryInfo(startDir)
    let mutable found = None
    while not (isNull current) && found.IsNone do
        let configFile = Path.Combine(current.FullName, "flappy.toml")
        if File.Exists configFile then
            found <- Some current.FullName
        else
            current <- current.Parent
    found

let tryGetVsRoot (path: string) =
    let mutable current = if File.Exists path then Path.GetDirectoryName(path) else path
    let mutable found = None
    while not (String.IsNullOrEmpty current) && found.IsNone do
        if Directory.Exists(Path.Combine(current, "VC", "Auxiliary", "Build")) then
            found <- Some current
        else
            current <- Path.GetDirectoryName(current)
    found

let patchCommandForMsvc (compiler: string) (args: string) (arch: string) : (string * string) option =
    let c = compiler.ToLower()
    let msvcCommands = ["cl"; "cl.exe"; "msvc"; "clang-cl"; "clang-cl.exe"; "lib"; "lib.exe"]
    
    // Check if the compiler is a standard command or an absolute path
    let isStandardMsvc = List.contains c msvcCommands || (c = "cmd.exe" && args.Contains("cl "))
    let vsRootFromPath = tryGetVsRoot compiler
    
    if isStandardMsvc || vsRootFromPath.IsSome then
        let vsPathOpt =
            if vsRootFromPath.IsSome then vsRootFromPath
            else findVsInstallPath()
            
        match vsPathOpt with
        | Some vsPath ->
            match getVcVarsAllPath vsPath with
            | Some vcvarsPath -> 
                let realCmd = if vsRootFromPath.IsSome then $"\"{compiler}\"" else compiler
                Some ("cmd.exe", $"/c \"call \"{vcvarsPath}\" {arch} && {realCmd} {args}\" ")
            | None -> None
        | None -> None
    else None