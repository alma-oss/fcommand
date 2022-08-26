module Lmc.Command.Test.CommandResponseCreated

open System
open System.Net
open Expecto
open FSharp.Data
open Lmc.Command
open Lmc.ServiceIdentification
open Lmc.ErrorHandling
open Lmc.Serializer

let orFail = function
    | Some value -> value
    | None -> failtestf "Value is not in correct format."

[<AutoOpen>]
module AssertSerializedEvent =
    let replace key (pattern: string) (replacement: string) (string: string) =
        let pattern = sprintf "%A: ?%A" key pattern
        let replacement = sprintf "%A: %A" key replacement
        System.Text.RegularExpressions.Regex.Replace(string, pattern, replacement)

    let replaceValue (value: string) (replacement: string) (string: string) =
        string.Replace(value, replacement)

    let splitLines (string: string) =
        string.Split(",")

    /// https://stackoverflow.com/questions/11040707/c-sharp-regex-for-guid
    let guidRegex = @"[{(]?[0-9a-f]{8}[-]?(?:[0-9a-f]{4}[-]?){3}[0-9a-f]{12}[)}]?"

    let timeRegex = @"\d{4}\-\d{2}\-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"

    let normalizeId =
        replace "id" guidRegex "<NORMALIZED-ID>"

    let normalizeIdValue idValue =
        replaceValue idValue "<NORMALIZED-ID>"

    let normalizeCreatedAt =
        replace "created_at" timeRegex "<NORMALIZED-TIME>"

    let normalizeTimestamp =
        replace "timestamp" timeRegex "<NORMALIZED-TIMESTAMP>"

    let normalizeAllIds =
        replace "correlation_id" guidRegex "<NORMALIZED-CORRELATION-ID>"
        >> replace "causation_id" guidRegex "<NORMALIZED-CAUSATION-ID>"

    let normalizeResourceHref =
        replace "href" ".*?" "<NORMALIZED_HREF>"

    let assertSerializedEvents description normalize (expected: string) (actual: string) =
        let normalize serialized =
            serialized
            |> normalize
            |> splitLines
            |> Seq.map (fun s -> s.Trim())
            |> Seq.toList

        let actualLines = actual |> normalize
        let expectedLines = expected |> normalize

        expectedLines
        |> List.iteri (fun i expected ->
            Expect.equal actualLines.[i] expected description
        )

type DoSomethingCommandMetaData = {
    CreatedAt: DateTime
}

type DoSomethingCommandData = {
    FirstName: DataItem<string>
    LastName: DataItem<string>
}

type DoSomethingCommandResponseDataDto = {
    FirstName: DataItemDto<string>
    LastName: DataItemDto<string>
    Age: DataItemDto<int>
}

type DoSomethingCommandResponseData = {
    FirstName: DataItem<string>
    LastName: DataItem<string>
    Age: DataItem<int>
}

type MetaDataDto = {
    CreatedAt: string
}

[<Tests>]
let createAndSerialize =
    testList "Command.Event - CommandResponseCreated" [
        testCase "create and serialize" <| fun _ ->
            let commandInput = """{
                "schema": 1,
                "id": "99c196e0-98c8-44f4-b6b0-77a80d6353cf ",
                "correlation_id": "89fb9f01-0c34-4a60-a607-c3d3d3fca02a",
                "causation_id": "148d14de-63c2-4539-a84d-389dfb40b326",
                "timestamp": "2020-05-12T12:50:25.983Z",
                "ttl": 100,
                "authentication_bearer": "",
                "request": "do_something",
                "reactor": {
                    "domain": "test",
                    "context": "reactor",
                    "purpose": "common",
                    "version": "*",
                    "zone": "*",
                    "bucket": "*"
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
                    "type": "kafka",
                    "identification": "test.commandStream.common.stable"
                },
                "meta_data": {
                    "created_at": "2020-05-12T12:50:25.983Z"
                },
                "data": {
                    "name": {
                        "value": "First",
                        "type": "string"
                    },
                    "last_name": {
                        "value": "Surname",
                        "type": "string"
                    }
                }
            }"""

            let expectedResponseEvent = """{
    "schema": 1,
    "id": "e41b1383-52c3-4dfd-8ddc-cfd081916a85",
    "correlation_id": "89fb9f01-0c34-4a60-a607-c3d3d3fca02a",
    "causation_id": "148d14de-63c2-4539-a84d-389dfb40b326",
    "timestamp": "2020-05-12T14:50:25.983Z",
    "event": "command_response_created",
    "domain": "test",
    "context": "deriver",
    "purpose": "common",
    "version": "stable",
    "zone": "all",
    "bucket": "test",
    "meta_data": {
        "created_at": "2020-11-30T15:24:05.818Z"
    },
    "key_data": {
        "command_id": "99c196e0-98c8-44f4-b6b0-77a80d6353cf"
    },
    "domain_data": {
        "response": {
            "schema": 1,
            "id": "e103fe50-68ac-407d-bdbb-0e5738f9f64c",
            "correlation_id": "89fb9f01-0c34-4a60-a607-c3d3d3fca02a",
            "causation_id": "148d14de-63c2-4539-a84d-389dfb40b326",
            "timestamp": "2020-05-12T14:50:25.983Z",
            "reactor": {
                "domain": "test",
                "context": "reactor",
                "purpose": "common",
                "version": "stable",
                "zone": "all",
                "bucket": "test"
            },
            "requestor": {
                "domain": "test",
                "context": "command",
                "purpose": "common",
                "version": "test",
                "zone": "all",
                "bucket": "common"
            },
            "meta_data": {
                "created_at": "2020-11-30T15:24:05.818Z"
            },
            "data": {
                "first_name": {
                    "value": "First",
                    "type": "string"
                },
                "last_name": {
                    "value": "Surname",
                    "type": "string"
                },
                "age": {
                    "value": 42,
                    "type": "int"
                }
            },
            "response_to": "kafka",
            "response": 200,
            "errors": []
        }
    }
}"""

            let parsedCommand =
                commandInput
                |> Command.parse
                    (fun metaData -> result {
                        let! createdAt =
                            match metaData with
                            | RawData.Item "created_at" (RawData createdAt) -> Ok (createdAt.AsDateTime())
                            | _ -> Error MissingData

                        let metaData: DoSomethingCommandMetaData = {
                            CreatedAt = createdAt
                        }

                        return metaData
                    })
                    (fun responseData -> result {
                        let! firstName =
                            match responseData with
                            | RawData.Item "name" (RawData.DataItem name) -> Ok name
                            | _ -> Error MissingData

                        let! lastName =
                            match responseData with
                            | RawData.Item "last_name" (RawData.DataItem lastName) -> Ok lastName
                            | _ -> Error MissingData

                        return Data {
                            FirstName = firstName
                            LastName = lastName
                        }
                    })

            match parsedCommand with
            | Error e -> failtestf "Parsing a command ends with %A" e
            | Ok doSomething ->
                let response =
                    let (Reactor reactorPattern) = doSomething.Reactor
                    let reactor =
                        Box.createFromValues
                            reactorPattern.Domain
                            reactorPattern.Context
                            (match reactorPattern.Purpose with PurposePattern.Purpose p -> p | _ -> Purpose "common")
                            (match reactorPattern.Version with VersionPattern.Version v -> v | _ -> Version "stable")
                            (match reactorPattern.Zone with ZonePattern.Zone z -> z | _ -> Zone "all")
                            (match reactorPattern.Bucket with BucketPattern.Bucket b -> b | _ -> Bucket "test")
                        |> ReactorResponse

                    let requestData = doSomething.Data |> Data.data
                    let responseData = Data {
                        FirstName = requestData.FirstName
                        LastName = requestData.LastName
                        Age = {
                            Value = 42
                            Type = "int"
                        }
                    }

                    CommandResponse.create
                        doSomething.CorrelationId
                        doSomething.CausationId
                        (doSomething.Timestamp |> DateTime.Parse)
                        reactor
                        doSomething.Requestor
                        doSomething.ReplyTo.Type
                        (StatusCode HttpStatusCode.OK)
                        []
                        (Some responseData)

                let deriver: Box = {
                    Domain = Domain "test"
                    Context = Context "deriver"
                    Purpose = Purpose "common"
                    Version = Version "stable"
                    Zone = Zone "all"
                    Bucket = Bucket "test"
                }

                let commandResponseCreated =
                    response
                    |> Event.CommandResponseCreated.deriveFromCommandResponse deriver doSomething.Id

                let serialized =
                    let serializeMetaData (GenericMetaData metaData): MetaDataDto =
                        match metaData |> Map.tryFind "created_at" with
                        | Some createdAt ->
                            {
                                CreatedAt = createdAt
                            }
                        | _ -> failtestf "Metadata has no field \"created_at\"."

                    let serializeData (Data data): DoSomethingCommandResponseDataDto =
                        {
                            FirstName = data.FirstName |> DataItemDto.serialize id
                            LastName = data.LastName |> DataItemDto.serialize id
                            Age = data.Age |> DataItemDto.serialize id
                        }

                    let serializeData = function
                        | Some data -> serializeData data
                        | _ -> failtestf "Response data was expected"

                    commandResponseCreated
                    |> Event.CommandResponseCreated.Event.serialize Serialize.toJsonPretty (CommandResponse.toDto serializeMetaData serializeData)

                match serialized with
                | Error e -> failtestf "Serializing command ends with %A" e
                | Ok serializedCommand ->
                    serializedCommand
                    |> assertSerializedEvents
                        "Serialized CommandResponseCreated.Event"
                        (normalizeId >> normalizeCreatedAt >> normalizeTimestamp)
                        expectedResponseEvent

                ("all,test", "CommandResponseCreated key should created with spot")
                ||> Expect.equal (commandResponseCreated |> Event.CommandResponseCreated.Event.key |> Lmc.Kafka.MessageKey.value)
    ]
