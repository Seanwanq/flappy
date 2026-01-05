module Flappy.Config

open System
open System.IO
open Tomlyn
open Tomlyn.Model

type PackageConfig = {
    Name: string
    Version: string
    Authors: string list
}

type BuildConfig = {
    Compiler: string
    Standard: string
    Output: string
}

type DependencySource = 
    | Git of url: string * tag: string option
    | Url of url: string
    | Local of path: string

type Dependency = {
    Name: string
    Source: DependencySource
}

type FlappyConfig = {
    Package: PackageConfig
    Build: BuildConfig
    Dependencies: Dependency list
}

let defaultConfig = 
    {
        Package = { Name = "untitled"; Version = "0.1.0"; Authors = [] }
        Build = { Compiler = "g++"; Standard = "c++17"; Output = "main" }
        Dependencies = []
    }

let parse (tomlContent: string) : Result<FlappyConfig, string> =
    try
        let model = Toml.ToModel(tomlContent)
        
        let getTable (key: string) (m: TomlTable) = 
            if m.ContainsKey(key) && m.[key] :? TomlTable then Some (m.[key] :?> TomlTable) else None

        let getString (key: string) (m: TomlTable) (def: string) =
            if m.ContainsKey(key) && m.[key] :? string then m.[key] :?> string else def
        
        let getOptString (key: string) (m: TomlTable) =
            if m.ContainsKey(key) && m.[key] :? string then Some (m.[key] :?> string) else None

        let getList (key: string) (m: TomlTable) =
            if m.ContainsKey(key) && m.[key] :? TomlArray then 
                (m.[key] :?> TomlArray) |> Seq.cast<obj> |> Seq.map string |> Seq.toList
            else []

        let packageConfig = 
            match getTable "package" model with
            | Some pkg -> 
                { Name = getString "name" pkg defaultConfig.Package.Name
                  Version = getString "version" pkg defaultConfig.Package.Version
                  Authors = getList "authors" pkg }
            | None -> defaultConfig.Package

        let buildConfig =
            match getTable "build" model with
            | Some build ->
                { Compiler = getString "compiler" build defaultConfig.Build.Compiler
                  Standard = getString "standard" build defaultConfig.Build.Standard
                  Output = getString "output" build defaultConfig.Build.Output }
            | None -> defaultConfig.Build

        let dependencies = 
            match getTable "dependencies" model with
            | Some deps ->
                deps.Keys 
                |> Seq.choose (fun key ->
                    if deps.[key] :? TomlTable then
                        let depTable = deps.[key] :?> TomlTable
                        let source = 
                            if depTable.ContainsKey("git") then
                                Some (Git (getString "git" depTable "", getOptString "tag" depTable))
                            elif depTable.ContainsKey("url") then
                                Some (Url (getString "url" depTable ""))
                            elif depTable.ContainsKey("path") then
                                Some (Local (getString "path" depTable ""))
                            else
                                None
                        match source with
                        | Some s -> Some { Name = key; Source = s }
                        | None -> None
                    else None
                )
                |> Seq.toList
            | None -> []

        Ok { Package = packageConfig; Build = buildConfig; Dependencies = dependencies }
    with
    | ex -> Error ex.Message