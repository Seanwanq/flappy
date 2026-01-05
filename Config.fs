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

type FlappyConfig = {
    Package: PackageConfig
    Build: BuildConfig
}

let defaultConfig = 
    {
        Package = { Name = "untitled"; Version = "0.1.0"; Authors = [] }
        Build = { Compiler = "g++"; Standard = "c++17"; Output = "main" }
    }

let parse (tomlContent: string) : Result<FlappyConfig, string> =
    try
        let model = Toml.ToModel(tomlContent)
        
        let getTable (key: string) (m: TomlTable) = 
            if m.ContainsKey(key) && m.[key] :? TomlTable then Some (m.[key] :?> TomlTable) else None

        let getString (key: string) (m: TomlTable) (def: string) =
            if m.ContainsKey(key) && m.[key] :? string then m.[key] :?> string else def

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

        Ok { Package = packageConfig; Build = buildConfig }
    with
    | ex -> Error ex.Message
