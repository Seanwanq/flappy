namespace Flappy.Core

open System
open System.Diagnostics
open System.Text

module Shell =
    let execute (cmd: string) (args: string) (workingDir: string option) (env: Map<string, string> option) =
        let psi = ProcessStartInfo(FileName = cmd, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true)
        if workingDir.IsSome then psi.WorkingDirectory <- workingDir.Value
        
        env |> Option.iter (fun e ->
            for kv in e do
                if psi.EnvironmentVariables.ContainsKey kv.Key then
                    psi.EnvironmentVariables.[kv.Key] <- kv.Value
                else
                    psi.EnvironmentVariables.Add(kv.Key, kv.Value))

        try
            use p = new Process()
            p.StartInfo <- psi
            let output = StringBuilder()
            let error = StringBuilder()
            
            p.OutputDataReceived.Add(fun e -> if not (String.IsNullOrEmpty e.Data) then output.AppendLine(e.Data) |> ignore)
            p.ErrorDataReceived.Add(fun e -> if not (String.IsNullOrEmpty e.Data) then error.AppendLine(e.Data) |> ignore)
            
            if not (p.Start()) then 
                Error $"Failed to start {cmd}"
            else
                p.BeginOutputReadLine()
                p.BeginErrorReadLine()
                p.WaitForExit()
                
                let outStr = output.ToString()
                let errStr = error.ToString()
                
                if p.ExitCode = 0 then Ok (outStr, errStr)
                else 
                    // Prefer error stream, fallback to output if error is empty
                    let msg = if String.IsNullOrWhiteSpace errStr then outStr else errStr
                    Error msg
        with ex -> Error $"Failed to run {cmd}: {ex.Message}"
