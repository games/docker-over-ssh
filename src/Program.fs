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
    abstract Start: string -> unit
    abstract Stop: unit -> unit


let errorHandler =
    ProcessExiter(
        colorizer =
            function
            | ErrorCode.HelpText -> None
            | _ -> Some ConsoleColor.Red
    )


let argsParser =
    ArgumentParser.Create<Arguments>(programName = "docker-over-ssh", errorHandler = errorHandler)


let (|Preparing|_|) (value: JSONMessage) =
    if value.Status = "Preparing" then Some value.ID else None

let (|Waiting|_|) (value: JSONMessage) =
    if value.Status = "Waiting" then Some value.ID else None

let (|HasError|_|) (value: JSONMessage) =
    if isNotNull value.Error then Some value.Error else None

let (|OnlyStatus|_|) (value: JSONMessage) =
    if isNull value.ID && isNotNull value.Status then
        Some value.Status
    else
        None

let (|LayerAlreadyExisted|_|) (value: JSONMessage) =
    if value.Status = "Layer already exists" then
        Some(value.ID, $"{value.ID} {value.Status}")
    else
        None

let (|OnGoing|_|) (value: JSONMessage) =
    if isNotNull value.Progress && value.Progress.Current < value.Progress.Total then
        Some(value.ID, float value.Progress.Current, float value.Progress.Total, $"{value.ID} {value.Status}")
    else
        None

let taskProgress (ctx: ProgressContext) =
    let tasks = Dictionary<string, ProgressTask>()
    let subTasks = Dictionary<string, ProgressTask>()
    let mutable current = Unchecked.defaultof<ProgressTask>

    let getOrCreateSubTask key =
        if not (subTasks.ContainsKey key) then
            subTasks[key] <- ctx.AddTask key
            current.MaxValue <- current.MaxValue + 1.0

        subTasks[key]

    { new ITaskProgress<JSONMessage> with
        member this.Start taskName =
            this.Stop()
            subTasks.Clear()
            current <- ctx.AddTask(taskName, maxValue = 0)
            tasks[taskName] <- current

        member _.Stop() =
            if isNotNull current then
                current.StopTask()

            for sub in subTasks do
                sub.Value.StopTask()

        member _.Report value =
            match value with
            | HasError error -> AnsiConsole.MarkupLine $"[red]ERROR:[/] {error}"
            | OnlyStatus status -> AnsiConsole.WriteLine(string status)
            | Preparing key ->
                let sub = getOrCreateSubTask key
                sub.Description <- "Preparing"
            | Waiting key ->
                let sub = getOrCreateSubTask key
                sub.Description <- "Waiting"
            | LayerAlreadyExisted(key, description) ->
                let sub = getOrCreateSubTask key
                sub.Description <- description
                sub.StopTask()
                current.Increment 1.0
            | OnGoing(key, current, total, description) ->
                let sub = getOrCreateSubTask key
                sub.Description <- description
                sub.MaxValue <- total
                sub.Value <- current
            | _ -> () }


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

        if not client.IsConnected then
            failwith "SSH connection failed"

        let forwardedPortLocal =
            new ForwardedPortLocal(localHostName, localPort, registryHost, registryPort)

        client.AddForwardedPort forwardedPortLocal
        forwardedPortLocal.Start()

        do! Task.Delay 500

        let docker = dockerClient ()

        for imageName in images do
            taskProgress.Start imageName
            do! docker.Images.PushImageAsync(imageName, ImagePushParameters(), AuthConfig(), taskProgress)
            taskProgress.Stop()
    }


[<EntryPoint>]
let main argv =
    AnsiConsole
        .Progress()
        .Columns(TaskDescriptionColumn(), ProgressBarColumn(), PercentageColumn(), SpinnerColumn())
        .StartAsync(fun ctx ->
            task {
                let args = argsParser.ParseCommandLine argv
                let proc = taskProgress ctx
                do! run args proc
            })
        .Wait()

    0
