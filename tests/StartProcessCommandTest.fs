module Alma.Command.Test.StartProcessCommand

open Expecto
open Alma.Command

let orFail = function
    | Some value -> value
    | None -> failtestf "Value is not in correct format."

[<AutoOpen>]
module ApplicationLogic =
    open Alma.ServiceIdentification

    type ActionRequestToken = ActionRequestToken of string

    let createProcessToGenerateTGT currentApplication reactor authentication ttl (service: Service, actionRequestToken: ActionRequestToken) =
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
    open Alma.ServiceIdentification

    let private box context =
        (Box.createFromStrings("test", context, "common", "test", "all", "common")).Value

    let currentApplication = box "command"
    let reactor =
        box "reactor"
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

open FSharp.Data
open Alma.Serializer

[<Tests>]
let createAndSerializeCommand =
    let parseMetadata data: Result<MetaData, _> =
        match data with
        | RawData.Item "created_at" (RawData createdAt) -> createdAt.AsDateTimeOffset().DateTime |> CreatedAt |> OnlyCreatedAt |> Ok
        | _ -> failtestf "Invalid metadata %A" data

    let parseData data: Result<Data<StartProcess.CommandData>, _> =
        let processItem =
            match data with
            | RawData.Item "process" (RawData.DataItem p) -> p |> DataItem.map StartProcess.ProcessName
            | _ -> failtestf "Invalid data.process %A" data

        let processVariables: DataItem<_> =
            match data with
            | RawData.Item "process_variables" processVariables ->
                let value =
                    match processVariables with
                    | RawData.Item "value" v -> v
                    | _ -> failtestf "Invalid data.process_variables.value %A" processVariables
                let valueType =
                    match processVariables with
                    | RawData.Item "type" (RawData t) -> t
                    | _ -> failtestf "Invalid data.process_variables.type %A" processVariables

                let art =
                    match value with
                    | RawData.Item "action_request_token" (RawData.DataItem art) ->
                        let art = art |> DataItem.map (ActionRequestToken >> (fun art -> art :> obj))

                        StartProcess.ProcessVariable.Object ("action_request_token", art)
                    | _ -> failtestf "Invalid data.process_variables.action_request_token %A" value

                let service =
                    match value with
                    | RawData.Item "service" (RawData.DataItem service) -> StartProcess.ProcessVariable.String ("service", service)
                    | _ -> failtestf "Invalid data.process_variables.service %A" value

                {
                    Value = [ service; art ]
                    Type = valueType.AsString()
                }

            | _ -> failtestf "Invalid data.process_variables %A" data

        Ok (Data {
            Process = processItem
            ProcessVariables = processVariables
        })

    testList "Command - Start Process" [
        testCase "create and serialize" <| fun _ ->
            provideCommand
            |> List.iter (fun (startProcessCommand, serializeObj, expected, description) ->
                let serializedCommand = startProcessCommand |> StartProcess.serialize serializeObj

                match serializedCommand, expected with
                | Ok dto, Ok expected ->
                    let serializedCommand = dto |> CommandDto.serialize Serialize.toJsonPretty

                    let actualLines = serializedCommand |> normalize
                    let expectedLines = expected |> normalize

                    expectedLines
                    |> List.iteri (fun i expected ->
                        Expect.equal actualLines.[i] expected description
                    )

                    let parsedCommand =
                        match serializedCommand |> Command.parse parseMetadata parseData with
                        | Ok parsed -> parsed
                        | Error error -> failtestf "Serialzed Command cannot be parsed again. %A" error

                    let startProcessCommand = startProcessCommand |> StartProcess.Command.command

                    // those objects are equal, but for some reason, they are not equal, so they are serialized to string and compared
                    Expect.equal
                        (parsedCommand |> sprintf "%A")
                        (startProcessCommand |> sprintf "%A")
                        "Serialized result parsed again"

                | Error error, Error expectedError ->
                    Expect.equal error expectedError description
                | Ok actual, Error error ->
                    failtestf "Serialize a command expects to fail with %A, but it created a %A." error actual
                | Error actual, Ok _ ->
                    failtestf "Serialize a command expects to succeed, but it ends up with error %A" actual
            )
    ]
