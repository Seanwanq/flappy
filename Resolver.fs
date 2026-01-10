module Flappy.Resolver

open System.IO
open Flappy.Config
open Flappy.DependencyManager

type ResolvedNode = {
    Name: string
    Dependency: Dependency
    Path: string
    Children: ResolvedNode list
}

// Helper for source equality
let isSameSource (d1: Dependency) (d2: Dependency) = 
    match d1.Source, d2.Source with
    | Git(u1, t1), Git(u2, t2) -> u1 = u2 && t1 = t2
    | Url u1, Url u2 -> u1 = u2
    | Local p1, Local p2 -> p1 = p2
    | _ -> false

let resolveOne (dep: Dependency) (profile: BuildProfile) (compiler: string) (arch: string) : Result<string * Dependency list, string> = 
    match fetch dep profile compiler arch with
    | Error e -> Error e
    | Ok path ->
        let tomlPath = Path.Combine(path, "flappy.toml")
        if File.Exists tomlPath then
            let content = File.ReadAllText tomlPath
            match Config.parse content None with
            | Ok config -> Ok (path, config.Dependencies)
            | Error e -> Error $"Failed to parse flappy.toml in {dep.Name}: {e}"
        else
            // No flappy.toml -> Leaf node (Raw library)
            Ok (path, [])

let resolveGraph (rootDeps: Dependency list) (profile: BuildProfile) (compiler: string) (arch: string) : Result<Map<string, ResolvedNode>, string> = 
    
    // State: 
    // - resolved: Map<Name, ResolvedNode> (The memoized results)
    // - path: Set<Name> (Current recursion stack for cycle detection)
    // - scopeMap: Map<Name, Dependency> (Available dependencies at the current level for bridging)
    
    let rec visit (deps: Dependency list) (scopeMap: Map<string, Dependency>) (resolved: Map<string, ResolvedNode>) (path: Set<string>) : Result<Map<string, ResolvedNode>, string> = 
        match deps with
        | [] -> Ok resolved
        | dep :: rest ->
            if path.Contains dep.Name then
                Error $"Cycle detected: {dep.Name} depends on itself (indirectly)."
            else
                match resolved.TryFind dep.Name with
                | Some existing ->
                    // Conflict Check (Strict Mode)
                    if not (isSameSource dep existing.Dependency) then
                        Error $"Version conflict detected for '{dep.Name}':\n1. {existing.Dependency.Source}\n2. {dep.Source}\nPlease ensure all dependencies use the same version of '{dep.Name}'."
                    else
                        // Already resolved and consistent, skip recursion
                        visit rest scopeMap resolved path
                | None ->
                    // Fetch and Parse
                    match resolveOne dep profile compiler arch with
                    | Error e -> Error e
                    | Ok (diskPath, nativeSubDeps) ->
                        // 6.5 Raw Dependency Bridging: Combine native sub-deps with manually specified ones from the parent
                        let bridgedSubDeps = 
                            dep.ExtraDependencies 
                            |> List.choose (fun name -> scopeMap.TryFind name)
                        
                        let allSubDeps = nativeSubDeps @ bridgedSubDeps |> List.distinctBy (fun d -> d.Name)
                        let childScopeMap = allSubDeps |> List.map (fun d -> d.Name, d) |> Map.ofList

                        // Recurse on children FIRST (DFS)
                        match visit allSubDeps childScopeMap resolved (path.Add dep.Name) with
                        | Error e -> Error e
                        | Ok childResolved ->
                            // Children are fully resolved. Now construct current node.
                            let childrenNodes = 
                                allSubDeps |> List.choose (fun d -> childResolved.TryFind d.Name)
                            
                            let node = { Name = dep.Name; Dependency = dep; Path = diskPath; Children = childrenNodes }
                            let newResolved = childResolved.Add(dep.Name, node)
                            
                            // Continue with siblings
                            visit rest scopeMap newResolved path

    let rootScopeMap = rootDeps |> List.map (fun d -> d.Name, d) |> Map.ofList
    visit rootDeps rootScopeMap Map.empty Set.empty

let topologicalSort (graph: Map<string, ResolvedNode>) : ResolvedNode list = 
    let mutable visited = Set.empty
    let mutable order = []
    
    let rec visit (node: ResolvedNode) = 
        if not (visited.Contains node.Name) then
            visited <- visited.Add node.Name
            for child in node.Children do
                visit child
            order <- node :: order

    for node in graph.Values do
        visit node
        
    List.rev order

let resolve (deps: Dependency list) (profile: BuildProfile) (compiler: string) (arch: string) : Result<ResolvedNode list, string> = 
    match resolveGraph deps profile compiler arch with
    | Ok graph -> Ok (topologicalSort graph)
    | Error e -> Error e