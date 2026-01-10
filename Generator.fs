module Flappy.Generator

open System
open System.IO
open System.Text.Json
open Flappy.Config
open Flappy.Builder

type CompileCommand = {
    directory: string
    command: string
    file: string
}

let generate (targetProfile: string option) =
    // Using Debug profile for generation by default as it usually contains more info (like symbols) 
    // and is what developers want for editing.
    match Builder.prepareBuild Debug targetProfile false with
    | Error e -> Error e
    | Ok ctx ->
        let includeFlags = if ctx.IsMsvc then ctx.IncludePaths |> List.map (fun p -> "/I\"" + p + "\"") |> String.concat " " else ctx.IncludePaths |> List.map (fun p -> "-I\"" + p + "\"") |> String.concat " "
        let defineFlags = if ctx.IsMsvc then ctx.Defines |> List.map (fun d -> "/D" + d) |> String.concat " " else ctx.Defines |> List.map (fun d -> "-D" + d) |> String.concat " "
        
        let isInterface (path: string) = let ext = Path.GetExtension(path).ToLower() in ext = ".ixx" || ext = ".cppm"
        
        let cwd = Directory.GetCurrentDirectory().Replace("\\", "/")
        
        let commands = 
            ctx.AllSources
            |> List.map (fun src ->
                let srcPath = Path.GetFullPath(src).Replace("\\", "/")
                let relPath = Path.GetRelativePath("src", src)
                let objExt = if ctx.IsMsvc then ".obj" else ".o"
                let objPath = Path.Combine(ctx.ObjBaseDir, relPath + objExt)
                
                let extraModuleFlags = if ctx.IsMsvc && isInterface src then "/interface" else ""
                
                let compileArgs = 
                    if ctx.IsMsvc then 
                         let pdbPath = Path.Combine(ctx.ObjBaseDir, "vc.pdb")
                         "/c /nologo /FS /std:" + ctx.Config.Build.Standard + " /EHsc " + ctx.ProfileFlags + " " + includeFlags + " " + defineFlags + " " + ctx.ArchFlags + " " + ctx.CustomFlags + " " + extraModuleFlags + " /Fo:\"" + objPath + "\" /Fd:\"" + pdbPath + "\" \"" + src + "\"" 
                    else 
                         "-c -std=" + ctx.Config.Build.Standard + " " + ctx.ProfileFlags + " " + includeFlags + " " + defineFlags + " " + ctx.ArchFlags + " " + ctx.CustomFlags + " -o \"" + objPath + "\" \"" + src + "\""
                
                let finalCmd, finalArgs = 
                    match Config.patchCommandForMsvc ctx.Compiler compileArgs ctx.Config.Build.Arch with 
                    | Some (c, a) -> (c, a) 
                    | None -> (ctx.Compiler, compileArgs)
                
                {
                    directory = cwd
                    command = finalCmd + " " + finalArgs
                    file = srcPath
                }
            )
            
        // Manual JSON generation to support Native AOT (System.Text.Json reflection is disabled)
        let sb = System.Text.StringBuilder()
        sb.AppendLine("[") |> ignore
        commands |> List.iteri (fun i cmd ->
            let escape (s: string) = s.Replace("\\", "\\\\").Replace("\"", "\\\"")
            sb.AppendLine("  {") |> ignore
            sb.AppendLine($"    \"directory\": \"{escape cmd.directory}\",") |> ignore
            sb.AppendLine($"    \"command\": \"{escape cmd.command}\",") |> ignore
            sb.AppendLine($"    \"file\": \"{escape cmd.file}\"") |> ignore
            if i < commands.Length - 1 then
                sb.AppendLine("  },") |> ignore
            else
                sb.AppendLine("  }") |> ignore
        )
        sb.AppendLine("]") |> ignore
        
        File.WriteAllText("compile_commands.json", sb.ToString())
        Log.info "Generated" "compile_commands.json"
        Ok ()
