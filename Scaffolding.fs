module Flappy.Scaffolding

open System.IO

let defaultToml (name: string) (compiler: string) = 
    $"""[package]
name = "{name}"
version = "0.1.0"
authors = ["Your Name"]

[build]
compiler = "{compiler}"
standard = "c++17"
output = "bin/{name}"
"""

let defaultMainCpp = """#include <iostream>

int main() {
    std::cout << "Hello, Flappy!" << std::endl;
    return 0;
}
"""

let initProject (name: string) (compiler: string) =
    let root = Directory.CreateDirectory(name)
    let src = root.CreateSubdirectory("src")
    
    let tomlPath = Path.Combine(root.FullName, "flappy.toml")
    let mainPath = Path.Combine(src.FullName, "main.cpp")

    if File.Exists(tomlPath) then
        printfn "Error: flappy.toml already exists."
    else
        File.WriteAllText(tomlPath, defaultToml name compiler)
        File.WriteAllText(mainPath, defaultMainCpp)
        printfn "Created new project `%s` with compiler `%s`" name compiler
