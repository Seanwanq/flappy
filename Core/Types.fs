namespace Flappy.Core

open System

type BuildProfile = Debug | Release

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
