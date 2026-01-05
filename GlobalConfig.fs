module Flappy.GlobalConfig

open System
open System.IO
open Tomlyn
open Tomlyn.Model

type GlobalConfig = {
    DefaultCompiler: string
}

let getConfigDir () =
    let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    let dir = Path.Combine(appData, "flappy")
    if not (Directory.Exists(dir)) then
        Directory.CreateDirectory(dir) |> ignore
    dir

let getConfigPath () =
    Path.Combine(getConfigDir(), "config.toml")

let saveConfig (config: GlobalConfig) =
    let path = getConfigPath()
    let toml = $"""default_compiler = "{config.DefaultCompiler}"
"""
    File.WriteAllText(path, toml)

let loadConfig () : GlobalConfig option =
    let path = getConfigPath()
    if File.Exists(path) then
        try
            let content = File.ReadAllText(path)
            let model = Toml.ToModel(content)
            if model.ContainsKey("default_compiler") && model.["default_compiler"] :? string then
                Some { DefaultCompiler = model.["default_compiler"] :?> string }
            else
                None
        with
        | _ -> None
    else
        None
