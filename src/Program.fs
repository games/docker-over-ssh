open System
open System.Collections.Generic
open System.Threading.Tasks
open Argu
open Docker.DotNet.Models
open Renci.SshNet
open Docker.DotNet
open Spectre.Console

let isNotNull x = x |> isNull |> not


type Arguments =
    | [<Mandatory>] SSH_Key of string
    | [<Mandatory>] SSH_Host of string
    | [<Mandatory>] SSH_User of string
    | Local_HostName of string
    | Local_Port of uint
    | [<Mandatory>] Registry_Host of string
    | Registry_Port of uint
    | [<Mandatory>] Image of string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | SSH_Key _ -> "The path of private key of the SSH user"
            | SSH_Host _ -> "SSH host"
            | SSH_User _ -> "SSH user"
            | Local_HostName _ -> "The hostname of the local machine itself, `localhost` is not working on Mac"
            | Local_Port _ -> "The local bound port, default is 5000"
            | Registry_Host _ -> "The hostname of the registry"
            | Registry_Port _ -> "The remote port, default is 5000"
            | Image _ -> "The docker images list"


type ITaskProgress<'T> =
    inherit IProgress<'T>
    abstract Start : string -> unit
    abstract Stop : unit -> unit


let errorHandler =
    ProcessExiter(
        colorizer =
            function
            | ErrorCode.HelpText -> None
            | _ -> Some ConsoleColor.Red
    )


let argsParser =
    ArgumentParser.Create<Arguments>(programName = "docker-over-ssh", errorHandler = errorHandler)


let taskProgress (ctx: ProgressContext) =
    let tasks = Dictionary<string, ProgressTask>()
    let subTasks = Dictionary<string, ProgressTask>()
    let mutable current = Unchecked.defaultof<string>
    { new ITaskProgress<JSONMessage> with
        member this.Start taskName =
            this.Stop ()
            subTasks.Clear ()
            tasks[taskName] <- ctx.AddTask taskName
            current <- taskName
        member _.Stop () =
            if isNotNull current && tasks.ContainsKey current then
                tasks[current].StopTask()
            for sub in subTasks do sub.Value.StopTask()
        member _.Report value =
            if isNotNull value.Error then
                AnsiConsole.MarkupLine $"[red]ERROR:[/] {value.ErrorMessage}"
            elif isNull value.ID then
                AnsiConsole.WriteLine $"{value.Status}"
            else
                let key = value.ID
                if not (subTasks.ContainsKey key) then
                    subTasks[key] <- ctx.AddTask key
                let subTask = subTasks[key]
                if isNotNull value.Progress then
                    subTask.Description <- $"{key} {value.Status}"
                    subTask.MaxValue <- float value.Progress.Total
                    subTask.Increment (float value.Progress.Current)
                if isNotNull value.Error then
                    AnsiConsole.MarkupLine $"[red]ERROR:[/] {value.ErrorMessage}"
        }


let dockerClient () =
    let cfg = new DockerClientConfiguration()
    cfg.CreateClient()


let run (args: ParseResults<Arguments>) (taskProgress: ITaskProgress<JSONMessage>) =
    task {
        let sshHost = args.GetResult SSH_Host
        let sshUser = args.GetResult SSH_User
        let localHostName = args.GetResult(Local_HostName, "localhost")
        let localPort = args.GetResult(Local_Port, 5000u)
        let registryHost = args.GetResult Registry_Host
        let registryPort = args.GetResult(Registry_Port, 5000u)
        let images = args.GetResults Image
        
        use privateKey = new PrivateKeyFile(args.GetResult SSH_Key)
        use client = new SshClient(sshHost, sshUser, privateKey)
        client.Connect()

        let forwardedPortLocal = new ForwardedPortLocal(localHostName, localPort, registryHost, registryPort)
        client.AddForwardedPort forwardedPortLocal
        forwardedPortLocal.Start()

        do! Task.Delay 500
        
        let docker = dockerClient ()
        for imageName in images do
            taskProgress.Start imageName
            do! docker.Images.PushImageAsync(
                imageName,
                ImagePushParameters(),
                AuthConfig(),
                taskProgress
            )
            taskProgress.Stop ()
    }


[<EntryPoint>]
let main argv =
    AnsiConsole
        .Progress()
        .Columns(TaskDescriptionColumn(),
            ProgressBarColumn(),
            PercentageColumn(),
            SpinnerColumn())
        .StartAsync(fun ctx -> task {
            let args = argsParser.ParseCommandLine argv
            let proc = taskProgress ctx
            do! run args proc
        }).Wait()
    0
