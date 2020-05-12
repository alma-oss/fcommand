module Lmc.Command.Test.StartProcessCommand

open Expecto
open Lmc.Command

let orFail = function
    | Some value -> value
    | None -> failtestf "Value is not in correct format."

[<AutoOpen>]
module ApplicationLogic =
    open ServiceIdentification

    type ActionRequestToken = ActionRequestToken of string

    let createProcessToGenerateTGT currentApplication reactor authentication ttl (service: ServiceIdentification.Service, actionRequestToken: ActionRequestToken) =
        [
            StartProcess.ProcessVariable.String ("service", (service |> Service.concat "-", "string") |> DataItem.createWithType)
            StartProcess.ProcessVariable.Object ("action_request_token", (actionRequestToken :> obj, "string") |> DataItem.createWithType)
        ]
        |> StartProcess.Command.create (StartProcess.ProcessName "Process_generate_TGT") (Requestor currentApplication) reactor authentication ttl {
            Type = "rest"
            Identification = ""
        }

    let serializeObj: obj -> obj = function
        | :? ActionRequestToken as actionRequestToken ->
            let (ActionRequestToken token) = actionRequestToken
            token :> obj
        | :? Service as service ->
            service |> Service.concat "-" :> obj
        | o -> o

[<AutoOpen>]
module TestLogic =
    open ServiceIdentification

    let currentApplication = Box.createFromStrings "test" "command" "common" "test" "all" "common"
    let reactor =
        Box.createFromStrings "test" "reactor" "common" "test" "all" "common"
        |> BoxPattern.ofBox
        |> Reactor

    let authentication = AuthenticationBearer.empty
    let ttl = TimeToLive.ofMiliSeconds 100

    let service = { Domain = Domain "domain"; Context = Context "context" }
    let actionRequestToken = ActionRequestToken "service-token"

    let normalize serialized =
        let replace key (pattern: string) (replacement: string) (string: string) =
            let pattern = sprintf "%A: %A" key pattern
            let replacement = sprintf "%A: %A" key replacement
            System.Text.RegularExpressions.Regex.Replace(string, pattern, replacement)

        let splitLines (string: string) =
            string.Split("\n")

        /// https://stackoverflow.com/questions/11040707/c-sharp-regex-for-guid
        let guidRegex = @"[{(]?[0-9a-f]{8}[-]?(?:[0-9a-f]{4}[-]?){3}[0-9a-f]{12}[)}]?"

        let timeRegex = @"\d{4}\-\d{2}\-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"

        serialized
        |> replace "id" guidRegex "id-uuid"
        |> replace "correlation_id" guidRegex "correlation_id-uuid"
        |> replace "causation_id" guidRegex "causation_id-uuid"
        |> replace "timestamp" timeRegex "time"
        |> replace "created_at" timeRegex "created-at"
        |> splitLines
        |> Seq.map (fun s -> s.Trim())
        |> Seq.toList

    let provideCommand =
        let createProcessToGenerateTGT = createProcessToGenerateTGT currentApplication reactor authentication ttl
        let expectedSerializedCommand =
            """{
                "schema": 1,
                "id": "a689e2f0-15dc-448d-b2f8-2ee45e09be52",
                "correlation_id": "a689e2f0-15dc-448d-b2f8-2ee45e09be52",
                "causation_id": "a689e2f0-15dc-448d-b2f8-2ee45e09be52",
                "timestamp": "2020-05-12T12:50:25.983Z",
                "ttl": 100,
                "authentication_bearer": "",
                "request": "start_process",
                    "reactor": {
                    "domain": "test",
                    "context": "reactor",
                    "purpose": "common",
                    "version": "test",
                    "zone": "all",
                    "bucket": "common"
                },
                "requestor": {
                    "domain": "test",
                    "context": "command",
                    "purpose": "common",
                    "version": "test",
                    "zone": "all",
                    "bucket": "common"
                },
                "reply_to": {
                    "type": "rest",
                    "identification": ""
                },
                "meta_data": {
                    "created_at": "2020-05-12T12:50:25.983Z"
                },
                "data": {
                    "process": {
                        "value": "Process_generate_TGT",
                        "type": "string"
                    },
                    "process_variables": {
                        "value": {
                            "action_request_token": {
                                "value": "service-token",
                                "type": "string"
                            },
                            "service": {
                                "value": "domain-context",
                                "type": "string"
                            }
                        },
                        "type": "json"
                    }
                }
            }"""

        [
            (service, actionRequestToken) |> createProcessToGenerateTGT, serializeObj, Ok expectedSerializedCommand, "StartProcess Command serialized."
        ]

open Lmc.Serializer

[<Tests>]
let createAndSerializeCommand =
    testList "Command - Start Process" [
        testCase "create and serialize" <| fun _ ->
            provideCommand
            |> List.iter (fun (startProcessCommand, serializeObj, expected, description) ->
                let serializedCommand = startProcessCommand |> StartProcess.serialize serializeObj

                match serializedCommand, expected with
                | Ok dto, Ok expected ->
                    let actualLines = dto |> CommandDto.serialize Serialize.toJsonPretty |> normalize
                    let expectedLines = expected |> normalize

                    expectedLines
                    |> List.iteri (fun i expected ->
                        Expect.equal actualLines.[i] expected description
                    )

                | Error error, Error expectedError ->
                    Expect.equal error expectedError description
                | Ok actual, Error error ->
                    failtestf "Serialize a command expects to fail with %A, but it created a %A." error actual
                | Error actual, Ok _ ->
                    failtestf "Serialize a command expects to succeed, but it ends up with error %A" actual
            )
    ]
