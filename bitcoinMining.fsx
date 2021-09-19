#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit"

open System
open System.Text
open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open System.Security.Cryptography

let gatorId = "anmol.patil"

let rec combinations acc size set = seq {
  match size, set with 
  | n, x::xs -> 
      if n > 0 then yield! combinations (x::acc) (n - 1) set
      if n >= 0 then yield! combinations acc n xs 
  | 0, [] -> yield acc 
  | _, [] -> () 
  }

let prefixZeroCount = 2


type FoundCoin = {
    Bitcoin: string
}

type Ping = {
    Message: string
}

type Work = {
    Message: string
    SequenceLength: int
    WorkerId: string
}

let computeHash(inputString: string) =     
    let randomByteString = inputString |> Encoding.ASCII.GetBytes
    let hash = (new SHA256Managed()).ComputeHash(randomByteString)
    let hashString = System.BitConverter.ToString(hash)
    hashString.Replace("-", "")


let startMining(randomString: string, actorId: string) =
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

let loopMining(seqLength: int, actorId: string) =
    let mutable isFound = false
    let combinationList = combinations [] seqLength ['a' .. 'z']
    for ele in combinationList do
        let combination = ele |> List.map string |> List.fold (+) ""
        let randomString = gatorId + ";" + combination
        let coin = startMining(randomString, actorId)
        if coin.Bitcoin <> "" then
            isFound <- true
    isFound


let stopMining(actorsList: ResizeArray<IActorRef>) = 
    for actor in actorsList do
        actor <! PoisonPill.Instance


let system = ActorSystem.Create("MineBitcoins")

type WorkerActor() = 
    inherit Actor()
    override x.OnReceive message = 
        let sender = x.Sender
        match message with
        | :? Work as w ->
            if w.Message.Equals("START") then
                printfn "Worker Started"
                let coin = loopMining(w.SequenceLength, w.WorkerId)
                if coin then
                    let newPing: Ping = {Message = "FOUND"}
                    sender <! newPing
        | _ -> printfn "Invalid response(Worker)"

type MasterActor() = 
    inherit Actor()
    let actorRefs = new ResizeArray<IActorRef>()
    let mutable seqLength = 2
    override x.OnReceive message = 
        let sender = x.Sender
        match message with
        | :? string as msg -> 
            if msg.Equals("START") then
                printfn "Master started"
                let foundCoin = loopMining(seqLength, "Master")
                if foundCoin then
                    stopMining(actorRefs) 
        | :? Ping as p ->
            if p.Message.Equals("AVAILABLE") then
                let work: Work = {
                    Message = "START"
                    SequenceLength = seqLength + 1
                    WorkerId = "Worker-" + string(seqLength)
                }
                seqLength <- seqLength + 1
                sender <! work
                actorRefs.Add(sender)
            else if p.Message.Equals("FOUND") then
                stopMining(actorRefs)
        | _ -> printfn "Invalid response(Master)"


let master = system.ActorOf(Props(typedefof<MasterActor>, Array.empty))
let worker1 = system.ActorOf(Props(typedefof<WorkerActor>, Array.empty))
let worker2 = system.ActorOf(Props(typedefof<WorkerActor>, Array.empty))
let worker3 = system.ActorOf(Props(typedefof<WorkerActor>, Array.empty))

let newPing: Ping = {
    Message = "AVAILABLE"
}

master <? "START" |> ignore
master.Tell(newPing, worker1)
master.Tell(newPing, worker2)
master.Tell(newPing, worker3)
    //TODO

// system.Terminate |> ignore
// exit(0)