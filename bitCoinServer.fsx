#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"

open System.Text
open Akka.Remote
open Akka.FSharp
open Akka.Actor
open System.Security.Cryptography

let configuration2 = 
    Configuration.parse
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote.helios.tcp {
                hostname = localhost
                port = 9001
            }
        }"
//10.20.215.10
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

let remoteSys = System.create "MineBitcoins" configuration2

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
                        remoteSys.Terminate() |> ignore
            | :? Ping as p ->
                if p.Message.Equals("AVAILABLE") then
                    let work: Work = {
                        Message = "START"
                        SequenceLength = seqLength + 1
                        WorkerId = "Worker-" + string(seqLength)
                        PrefixZeroesLength = prefixZeroCount
                    }
                    seqLength <- seqLength + 1
                    sender <! work
                    actorRefs.Add(sender)
                else if p.Message.Equals("FOUND") then
                    foundCoin <- true
                    remoteSys.ActorSelection("/user/worker*") <! PoisonPill.Instance
                    remoteSys.Terminate() |> ignore
                    remoteSys.Terminate() |> ignore
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
                remoteSys.Stop(mailbox.Self)
    | :? string as msg -> printfn "%s" msg
    | _ -> printfn "Invalid response(Worker)"


System.Console.ReadLine() |> ignore
remoteSys.WhenTerminated.Wait()    