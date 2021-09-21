#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"

open System.Text
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
                hostname = localhost
                port = 0
            }
        }"

let gatorId = "anmol.patil"

let mutable seqLength = 0
let mutable prefixZeroCount = 0
let mutable foundCoin = false
let actorRefs = new ResizeArray<IActorRef>()

let rec combinations acc size set = seq {
  match size, set with 
  | n, x::xs -> 
      if n > 0 then yield! combinations (x::acc) (n - 1) set
      if n >= 0 then yield! combinations acc n xs 
  | 0, [] -> yield acc 
  | _, [] -> () 
  }

type FoundCoin = {
    Bitcoin: string
}

type Ping = {
    Message: string
}

let newPing: Ping = {
    Message = "AVAILABLE"
}

type Work = {
    Message: string
    SequenceLength: int
    WorkerId: string
    PrefixZeroesLength: int
}

let computeHash(inputString: string) =     
    let randomByteString = inputString |> Encoding.ASCII.GetBytes
    let hash = (new SHA256Managed()).ComputeHash(randomByteString)
    let hashString = System.BitConverter.ToString(hash)
    hashString.Replace("-", "")

let startMining(randomString: string, prefixZeroCount: int, actorId: string) =
    let hashValue = computeHash(randomString)
    let emptyCoin: FoundCoin = {
        Bitcoin = ""
    }
    let prefix = String.replicate prefixZeroCount "0"
    if hashValue.StartsWith(prefix) then
        printfn "The bit coin for string %s is -> %s, found by %s" randomString hashValue actorId
        let bitCoin: FoundCoin = {
              Bitcoin = hashValue
        }
        bitCoin
    else
        emptyCoin

let loopMining(seqLength: int, prefixZeroCount: int, actorId: string) =
    let mutable isFound = false
    let combinationList = combinations [] seqLength ['a' .. 'z']
    let x = combinationList.GetEnumerator() 
    // while x.MoveNext() && not isFound do
    //     let combination = x.Current |> List.map string |> List.fold (+) ""
    //     let randomString = gatorId + ";" + combination
    //     let coin = startMining(randomString, prefixZeroCount, actorId)
    //     if coin.Bitcoin <> "" then
    //         isFound <- true
    while x.MoveNext() do
        let combination = x.Current |> List.map string |> List.fold (+) ""
        let randomString = gatorId + ";" + combination
        let coin = startMining(randomString, prefixZeroCount, actorId)
        if coin.Bitcoin <> "" then
            isFound <- true
    x.Dispose()
    isFound

let stopMining(actorsList: ResizeArray<IActorRef>) = 
    for actor in actorsList do
        actor <! PoisonPill.Instance
        
let stopMining2(actors: ActorSelection) = 
    actors <! PoisonPill.Instance

let system = System.create "local-system" configuration1

let handleMasterMessage(mailbox: Actor<obj>) msg =
        let sender = mailbox.Sender()
        match box msg with
            | :? Work as w -> 
                if w.Message.Equals("START") then
                    seqLength <- w.SequenceLength
                    prefixZeroCount <- w.PrefixZeroesLength
                    let foundCoin = loopMining(w.SequenceLength, w.PrefixZeroesLength, "Master")
                    if foundCoin then
                        stopMining(actorRefs)
                        system.Terminate() |> ignore
            | :? Ping as p ->
                if p.Message.Equals("AVAILABLE") then
                    let work: Work = {
                        Message = "START"
                        SequenceLength = seqLength + 1
                        WorkerId = "Local-Worker-" + string(seqLength)
                        PrefixZeroesLength = prefixZeroCount
                    }
                    seqLength <- seqLength + 1
                    sender <! work
                    actorRefs.Add(sender)
                else if p.Message.Equals("FOUND") then
                    foundCoin <- true
                    system.ActorSelection("/user/worker*") <! PoisonPill.Instance
                    system.Terminate() |> ignore
            | _ -> printfn "Invalid response(Master)"

let handleWorkerMessage(mailbox: Actor<obj>) msg =
    let sender = mailbox.Sender()
    match box msg with
    | :? Work as w ->
        if w.Message.Equals("START") then
            let coin = loopMining(w.SequenceLength, w.PrefixZeroesLength, w.WorkerId)
            if coin then
                let newPing: Ping = {Message = "FOUND"}
                sender <! newPing
            else
                printfn "Stopping self"
                system.Stop(mailbox.Self)
    | :? string as msg -> printfn "%s" msg
    | _ -> printfn "Invalid response(Worker)"

let deployRemotely address = Deploy(RemoteScope (Address.Parse address))  
let spawnRemote systemOrContext remoteSystemAddress actorName expr =  
    spawne systemOrContext actorName expr [SpawnOption.Deploy (deployRemotely remoteSystemAddress)]


let main(a: int, b: int) =
    let masterRef =  
        spawnRemote system "akka.tcp://MineBitcoins@localhost:9001/" "master" 
            <@ actorOf2(handleMasterMessage)@>

    let masterWork: Work = {
            Message = "START"
            SequenceLength = a
            WorkerId = "MASTER"
            PrefixZeroesLength = b 
        }

    masterRef <? masterWork |> ignore

    for i in 1 .. 10 do
        if not foundCoin then
            let workerId = "local-worker-" + string(i)
            // let workerRef = spawn system  workerId (actorOf2(handleWorkerMessage))
            let workerRef = spawnRemote system "akka.tcp://MineBitcoins@localhost:9001/" workerId <@ actorOf2(handleWorkerMessage)@>
            masterRef.Tell(newPing, workerRef)
    
    
    System.Console.ReadLine() |> ignore



match fsi.CommandLineArgs with 
    | [|_; stringSeqLength; prefixZeroes|] -> main(int(stringSeqLength), int(prefixZeroes))
    | _ -> printfn "Error: Invalid Arguments. End and Window size values must be passed."      