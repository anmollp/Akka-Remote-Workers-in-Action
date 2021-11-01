#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"

open System
open Akka.FSharp
open Akka.Configuration
open System.Security.Cryptography


let mutable ipAddress = ""

match fsi.CommandLineArgs with 
    | [|_; ip|] -> ipAddress <- ip
    | _ -> printfn "Error: Invalid Arguments."


let configuration =
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            }
            remote.helios.tcp {
                hostname = localhost
                port = 9000
            }
        }"
    )

let gatorId = "anmol.patil"

let getRandomString() = 
    gatorId + ";" + Guid.NewGuid().ToString()

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
        
let remoteSys = System.create "MineBitcoins" configuration

let handleWorkerMessage(mailbox: Actor<obj>) msg =
    let sender = mailbox.Sender()
    match box msg with
    | :? Work as w ->
        if w.Message.Equals("START") then
            let coin = loopMining(w.PrefixZeroesLength)
            if coin.Bitcoin <> "" then
                sender <! coin
    | :? string as msg ->
        if msg.Equals("STOP") then mailbox.Context.System.Terminate() |> ignore
    | _ -> printfn "Invalid response(Worker)"

let master = remoteSys.ActorSelection("akka.tcp://local-system@"+ipAddress+":2552/user/master")
    
for i in 1 .. 7 do
    let workerId = "remote-worker-" + string(i)
    let workerRef = spawn remoteSys  workerId (actorOf2(handleWorkerMessage))
    master.Tell(newPing, workerRef)

Console.ReadLine() |> ignore   