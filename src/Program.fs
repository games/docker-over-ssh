﻿open System
open System.Threading.Tasks
open Argu
open Docker.DotNet.Models
open Renci.SshNet
open Docker.DotNet

let isNotNull x = x |> isNull |> not


type Arguments =
    | [<Mandatory>] SSH_Key of string
    | [<Mandatory>] SSH_Host of string
    | [<Mandatory>] SSH_User of string
    | Local_HostName of string
    | Local_Port of uint
    | [<Mandatory>] Registry_Host of string
    | Registry_Port of uint
    | [<Mandatory>] Image_Id of string
    | Image_Tag of string

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
            | Image_Id _ -> "The docker image ID"
            | Image_Tag _ -> "The docker image tag, default is `latest`"

let errorHandler =
    ProcessExiter(
        colorizer =
            function
            | ErrorCode.HelpText -> None
            | _ -> Some ConsoleColor.Red
    )

let argsParser =
    ArgumentParser.Create<Arguments>(programName = "docker-over-ssh", errorHandler = errorHandler)


let messageProcess =
    { new IProgress<JSONMessage> with
        member _.Report value =
            printfn "report: %s %A" value.ID value.Status

            if isNotNull value.Progress then
                printfn
                    " progress: %s - %f"
                    value.ProgressMessage
                    (float value.Progress.Current / float value.Progress.Total)

            if isNotNull value.Error then
                printfn " error: %i - %s" value.Error.Code value.ErrorMessage }

let dockerClient () =
    let cfg = new DockerClientConfiguration()
    cfg.CreateClient()

let run (args: ParseResults<Arguments>) =
    task {
        let sshHost = args.GetResult SSH_Host
        let sshUser = args.GetResult SSH_User
        let localHostName = args.GetResult(Local_HostName, "localhost")
        let localPort = args.GetResult(Local_Port, 5000u)
        let registryHost = args.GetResult Registry_Host
        let registryPort = args.GetResult(Registry_Port, 5000u)
        let imageId = args.GetResult Image_Id
        let imageTag = args.GetResult(Image_Tag, "latest")
        let imageName = $"{localHostName}:{localPort}/{imageId}:{imageTag}"
        
        use privateKey = new PrivateKeyFile(args.GetResult SSH_Key)
        use client = new SshClient(sshHost, sshUser, privateKey)
        client.Connect()

        let forwardedPortLocal = new ForwardedPortLocal(localHostName, localPort, registryHost, registryPort)
        client.AddForwardedPort forwardedPortLocal
        forwardedPortLocal.Start()

        do! Task.Delay 1000
        
        let docker = dockerClient ()
        do!
            docker.Images.PushImageAsync(
                imageName,
                ImagePushParameters(),
                AuthConfig(),
                messageProcess
            )

        printfn "Done, press any key quit the tools"
        Console.Read() |> ignore
    }


[<EntryPoint>]
let main argv =
    let args = argsParser.ParseCommandLine argv
    run(args).Wait()
    0
