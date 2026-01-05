module Flappy.Scaffolding

open System.IO

type InitOptions = {
    Name: string
    Compiler: string
    Standard: string
    Arch: string
    Type: string
}

let defaultToml (options: InitOptions) = 
    $"""[package]
name = "{options.Name}"
version = "0.1.0"
authors = ["Your Name"]

[build]
compiler = "{options.Compiler}"
standard = "{options.Standard}"
arch = "{options.Arch}"
type = "{options.Type}"
output = "bin/{options.Name}"
"""

let defaultMainCpp = """#include <iostream>

int main() {
    std::cout << "Hello, Flappy!" << std::endl;
    return 0;
}
"""

let initProject (options: InitOptions) =
    let root = Directory.CreateDirectory(options.Name)
    let src = root.CreateSubdirectory("src")
    
    let tomlPath = Path.Combine(root.FullName, "flappy.toml")
    let mainPath = Path.Combine(src.FullName, "main.cpp")

    if File.Exists(tomlPath) then
        printfn "Error: flappy.toml already exists."
    else
        File.WriteAllText(tomlPath, defaultToml options)
        File.WriteAllText(mainPath, defaultMainCpp)
        printfn "Created new project `%s` [%s, %s] with compiler `%s` (%s)" options.Name options.Type options.Arch options.Compiler options.Standard