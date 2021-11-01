#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"

open System
open Akka.Remote
open Akka.FSharp
open Akka.Actor
open System.Security.Cryptography

let configuration1 =
    Configuration.parse
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote.helios.tcp {
                hostname = 10.20.215.4
                port = 2552
            }
        }"

let system = System.create "local-system" configuration1

let gatorId = "anmol.patil"

let getRandomString() = 
    gatorId + ";" + Guid.NewGuid().ToString()

let mutable prefixZeroCount = 0

let actorPaths = new ResizeArray<string>()

type FoundCoin = {
    Bitcoin: string
    String: string
}

type Ping = {
    Message: string
}

let newPing: Ping = {
    Message = "AVAILABLE"
}

type Work = {
    Message: string
    PrefixZeroesLength: int
}

let computeHash(inputString: string) =     
    let randomByteString = inputString |> Text.Encoding.ASCII.GetBytes
    let hash = (new SHA256Managed()).ComputeHash(randomByteString)
    let hashString = System.BitConverter.ToString(hash)
    hashString.Replace("-", "")

let startMining(randomString: string, prefixZeroCount: int) =
    let hashValue = computeHash(randomString)
    let emptyCoin: FoundCoin = {
        Bitcoin = ""
        String = randomString
    }
    let prefix = String.replicate prefixZeroCount "0"
    if hashValue.StartsWith(prefix) then
        let bitCoin: FoundCoin = {
              Bitcoin = hashValue
              String = randomString
        }
        bitCoin
    else
        emptyCoin

let loopMining(prefixZeroCount: int) =
    let mutable isFound = false
    let mutable coin: FoundCoin = {
        Bitcoin = ""
        String = ""
    }
    while not isFound do
        coin <- startMining(getRandomString(), prefixZeroCount)
        if coin.Bitcoin <> "" then
            isFound <- true
    coin

let stopMining(actorsList: ResizeArray<string>) = 
    for actor in actorsList do
        let x = system.ActorSelection(actor)
        x <! "STOP"
        
let handleMasterMessage(mailbox: Actor<obj>) msg =
    let sender = mailbox.Sender()
    match box msg with
        | :? Work as w -> 
            if w.Message.Equals("START") then
                let coin = loopMining(w.PrefixZeroesLength)
                if coin.Bitcoin <> "" then
                    printfn "%s\t%s" coin.String coin.Bitcoin
                    stopMining(actorPaths)
            else if w.Message.Equals("INITIATE") then   
                prefixZeroCount <- w.PrefixZeroesLength
                let masterWork: Work = {
                    Message = "START"
                    PrefixZeroesLength = prefixZeroCount 
                }
                mailbox.Self.Tell(masterWork)
        | :? Ping as p ->
            if p.Message.Equals("AVAILABLE") then
                actorPaths.Add(sender.Path.ToString())
                let work: Work = {
                    Message = "START"
                    PrefixZeroesLength = prefixZeroCount
                }
                sender <! work
        | :? FoundCoin as f ->
            printfn "%s\t%s" f.String f.Bitcoin
            stopMining(actorPaths)
            mailbox.Context.System.Terminate() |> ignore 
        | _ -> printfn "Invalid response(Master)"

let handleWorkerMessage(mailbox: Actor<obj>) msg =
    let sender = mailbox.Sender()
    match box msg with
    | :? Work as w ->
        if w.Message.Equals("START") then
            let coin = loopMining(w.PrefixZeroesLength)
            if coin.Bitcoin <> "" then
                sender <! coin
    | :? string as msg ->
        if msg.Equals("STOP") then
            mailbox.Context.System.Terminate() |> ignore 
    | _ -> printfn "Invalid response(Worker)"

let main(b: int) = 
    let masterRef = spawn system "master" (actorOf2(handleMasterMessage))

    let masterWork: Work = {
            Message = "INITIATE"
            PrefixZeroesLength = b 
        }

    masterRef <? masterWork |> ignore

    for i in 1 .. 3 do
        let workerId = "local-worker-" + string(i)
        let workerRef = spawn system  workerId (actorOf2(handleWorkerMessage))
        masterRef.Tell(newPing, workerRef)

    system.WhenTerminated.Wait()


match fsi.CommandLineArgs with 
    | [|_; prefixZeroes|] -> main(int(prefixZeroes))
    | _ -> printfn "Error: Invalid Arguments."      