namespace Lmc.Command

open System
open System.Net
open ServiceIdentification

// Simple types

type CommandId = CommandId of Guid

[<RequireQualifiedAccess>]
module CommandId =
    let value (CommandId id) = id

type CorrelationId = CorrelationId of Guid

[<RequireQualifiedAccess>]
module CorrelationId =
    let value (CorrelationId correlationId) = correlationId
    let fromCommandId = CommandId.value >> CorrelationId

type CausationId = CausationId of Guid

[<RequireQualifiedAccess>]
module CausationId =
    let value (CausationId causationId) = causationId
    let fromCommandId = CommandId.value >> CausationId

type Reactor = Reactor of BoxPattern
type ReactorResponse = ReactorResponse of Box

type Requestor = Requestor of Box

type ReplyTo = {
    Type: string
    Identification: string
}

type [<Measure>] MiliSecond
type TimeToLive = TimeToLive of int<MiliSecond>

[<RequireQualifiedAccess>]
module TimeToLive =
    let ofMiliSeconds value = TimeToLive (value * 1<MiliSecond>)
    let ofSeconds value = TimeToLive (value * 1000<MiliSecond>)

    let value (TimeToLive value) = int value

type AuthenticationBearer = AuthenticationBearer of string

[<RequireQualifiedAccess>]
module AuthenticationBearer =
    let empty = AuthenticationBearer ""

    let value (AuthenticationBearer value) = value

type Request = private Request of string

type RequestError =
    | EmptyRequest

[<RequireQualifiedAccess>]
module RequestError =
    let format = function
        | EmptyRequest -> "Request cannot be empty. It defines what you want command to do."

[<RequireQualifiedAccess>]
module Request =
    let create = function
        | null | "" -> Error EmptyRequest
        | request -> Ok (Request request)

    let value (Request value) = value

// Generic Command Data

type DataItem<'Value> = {
    Value: 'Value
    Type: string
}

[<RequireQualifiedAccess>]
module DataItem =
    let createWithType<'Value> (value: 'Value, valueType) =
        {
            Value = value
            Type = valueType
        }

    let create: 'Value -> DataItem<'Value> =
        fun value -> {
            Value = value
            Type = value.GetType().ToString()
        }

type Data<'CommandData> = Data of 'CommandData

//
// Generic Command
//

type SynchronousCommand<'MetaData, 'CommandData> = {
    Schema: int
    Id: CommandId
    CorrelationId: CorrelationId
    CausationId: CausationId
    Timestamp: string

    TimeToLive: TimeToLive
    AuthenticationBearer: AuthenticationBearer
    Request: Request

    Reactor: Reactor
    Requestor: Requestor

    MetaData: 'MetaData
    Data: Data<'CommandData>
}

type AsynchronousCommand<'MetaData, 'CommandData> = {
    Schema: int
    Id: CommandId
    CorrelationId: CorrelationId
    CausationId: CausationId
    Timestamp: string

    TimeToLive: TimeToLive
    AuthenticationBearer: AuthenticationBearer
    Request: Request

    Reactor: Reactor
    Requestor: Requestor
    ReplyTo: ReplyTo

    MetaData: 'MetaData
    Data: Data<'CommandData>
}

type Command<'MetaData, 'CommandData> =
    | Synchronous of SynchronousCommand<'MetaData, 'CommandData>
    | Asynchronous of AsynchronousCommand<'MetaData, 'CommandData>

//
// Command Response
//

type ResponseId = ResponseId of Guid

type StatusCode = StatusCode of HttpStatusCode

type ResponseError = {
    Status: StatusCode
    Title: string
    Detail: string
    // Source: ?
}

[<RequireQualifiedAccess>]
module ResponseError =
    let format = function
        | { Title = null; Detail = detail }
        | { Title = ""; Detail = detail } ->
            detail

        | { Title = title; Detail = detail } ->
            sprintf "%s (%s)" detail title

type CommandResponse<'MetaData, 'ResponseData> = {
    Schema: int
    Id: ResponseId
    CorrelationId: CorrelationId
    CausationId: CausationId
    Timestamp: string

    Reactor: ReactorResponse
    Requestor: Requestor

    MetaData: 'MetaData
    Data: Data<'ResponseData> option

    ResponseTo: string
    Response: StatusCode
    Errors: ResponseError list
}

type CommandResponseError<'MetaData, 'ResponseData> =
    | ParseError of string * exn
    | ErrorResponse of CommandResponse<'MetaData, 'ResponseData>

[<RequireQualifiedAccess>]
module CommandResponse =
    open FSharp.Data

    type NotParsed = NotParsed

    type private CommandResponseSchema = JsonProvider<"src/schema/response.json", SampleIsList = true>

    let private formatDateTime (dateTimeOffset: DateTimeOffset) =
        dateTimeOffset.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")

    let parseHttpStatusCode (code: int): HttpStatusCode = enum code

    let parse serializedResponse = result {
        try
            let rawResponse =
                serializedResponse
                |> CommandResponseSchema.Parse

            let data = rawResponse.Data.Attributes

            let response: CommandResponse<NotParsed, NotParsed> =
                {
                    Schema = data.Schema
                    Id = ResponseId data.Id
                    CorrelationId = CorrelationId data.CorrelationId
                    CausationId = CausationId data.CausationId
                    Timestamp = data.Timestamp |> formatDateTime

                    Reactor =
                        ReactorResponse (
                            Box.createFromStrings
                                data.Reactor.Domain
                                data.Reactor.Context
                                data.Reactor.Purpose
                                data.Reactor.Version
                                data.Reactor.Zone
                                data.Reactor.Bucket
                        )
                    Requestor =
                        Requestor (
                            Box.createFromStrings
                                data.Requestor.Domain
                                data.Requestor.Context
                                data.Requestor.Purpose
                                data.Requestor.Version
                                data.Requestor.Zone
                                data.Requestor.Bucket
                        )

                    MetaData = NotParsed
                    Data = None

                    ResponseTo = data.ResponseTo
                    Response = StatusCode (parseHttpStatusCode data.Response)
                    Errors =
                        data.Errors
                        |> Seq.map (fun e ->
                            {
                                Detail = e.Detail
                                Status = StatusCode (parseHttpStatusCode e.Status)
                                Title =
                                    match e.Title with
                                    | Some title -> title
                                    | _ -> ""
                            }
                        )
                        |> Seq.toList
                }

            return!
                match response.Response, response.Errors with
                | (StatusCode code), [] when (int code) < 400 -> Ok response
                | _ -> Error (ErrorResponse response)
        with
        | e ->
            return! Error (ParseError (serializedResponse, e))
    }

//
// Command DTO
//

type ReactorDto = {
    Domain: string
    Context: string
    Purpose: string
    Version: string
    Zone: string
    Bucket: string
}

type RequestorDto = {
    Domain: string
    Context: string
    Purpose: string
    Version: string
    Zone: string
    Bucket: string
}

type ReplyToDto = {
    Type: string
    Identification: string
}

// Generic command data DTO

type DataItemDto<'Value> = {
    Value: 'Value
    Type: string
}

[<RequireQualifiedAccess>]
module DataItemDto =
    let serialize serialize (item: DataItem<_>): DataItemDto<_> =
        {
            Value = item.Value |> serialize
            Type = item.Type
        }

// Generic Command DTO

type SynchronousCommandDto<'MetaDataDto, 'DataDto> = {
    Schema: int
    Id: Guid
    CorrelationId: Guid
    CausationId: Guid
    Timestamp: string

    Ttl: int
    AuthenticationBearer: string
    Request: string

    Reactor: ReactorDto
    Requestor: RequestorDto

    MetaData: 'MetaDataDto
    Data: 'DataDto
}

type AsynchronousCommandDto<'MetaDataDto, 'DataDto> = {
    Schema: int
    Id: Guid
    CorrelationId: Guid
    CausationId: Guid
    Timestamp: string

    Ttl: int
    AuthenticationBearer: string
    Request: string

    Reactor: ReactorDto
    Requestor: RequestorDto
    ReplyTo: ReplyToDto

    MetaData: 'MetaDataDto
    Data: 'DataDto
}

type CommandDto<'MetaDataDto, 'DataDto> =
    | Synchronous of SynchronousCommandDto<'MetaDataDto, 'DataDto>
    | Asynchronous of AsynchronousCommandDto<'MetaDataDto, 'DataDto>

//
// Modules
//

[<RequireQualifiedAccess>]
module Reactor =
    let internal serialize (Reactor boxPattern): ReactorDto =
        {
            Domain = boxPattern.Domain |> Domain.value
            Context = boxPattern.Context |> Context.value
            Purpose = boxPattern.Purpose |> PurposePattern.value
            Version = boxPattern.Version |> VersionPattern.value
            Zone = boxPattern.Zone |> ZonePattern.value
            Bucket = boxPattern.Bucket |> BucketPattern.value
        }

[<RequireQualifiedAccess>]
module Requestor =
    let internal serialize (Requestor box): RequestorDto =
        {
            Domain = box.Domain |> Domain.value
            Context = box.Context |> Context.value
            Purpose = box.Purpose |> Purpose.value
            Version = box.Version |> Version.value
            Zone = box.Zone |> Zone.value
            Bucket = box.Bucket |> Bucket.value
        }

[<RequireQualifiedAccess>]
module ReplyTo =
    let internal serialize (replyTo: ReplyTo): ReplyToDto =
        {
            Type = replyTo.Type
            Identification = replyTo.Identification
        }

[<RequireQualifiedAccess>]
module Data =
    let data (Data data) = data

//
// Serialize Command
//

type SerializeCommand<'MetaData, 'Data, 'MetaDataDto, 'DataDto, 'Error> =
    ('MetaData -> Result<'MetaDataDto, 'Error>) -> ('Data -> Result<'DataDto, 'Error>)
        -> Command<'MetaData, 'Data>
        -> Result<CommandDto<'MetaDataDto, 'DataDto>, 'Error>

[<RequireQualifiedAccess>]
module Command =
    let toDto: SerializeCommand<'MetaData, 'Data, 'MetaDataDto, 'DataDto, 'Error> =
        fun serializeMetadata serializeData -> function
        | Command.Synchronous command ->
            result {
                let! metaData = command.MetaData |> serializeMetadata
                let! data = command.Data |> Data.data |> serializeData

                return Synchronous {
                    Schema = command.Schema
                    Id = command.Id |> CommandId.value
                    CorrelationId = command.CorrelationId |> CorrelationId.value
                    CausationId = command.CausationId |> CausationId.value
                    Timestamp = command.Timestamp

                    Ttl = command.TimeToLive |> TimeToLive.value
                    AuthenticationBearer = command.AuthenticationBearer |> AuthenticationBearer.value
                    Request = command.Request |> Request.value

                    Reactor = command.Reactor |> Reactor.serialize
                    Requestor = command.Requestor |> Requestor.serialize

                    MetaData = metaData
                    Data = data
                }
            }
        | Command.Asynchronous command ->
            result {
                let! metaData = command.MetaData |> serializeMetadata
                let! data = command.Data |> Data.data |> serializeData

                return Asynchronous {
                    Schema = command.Schema
                    Id = command.Id |> CommandId.value
                    CorrelationId = command.CorrelationId |> CorrelationId.value
                    CausationId = command.CausationId |> CausationId.value
                    Timestamp = command.Timestamp

                    Ttl = command.TimeToLive |> TimeToLive.value
                    AuthenticationBearer = command.AuthenticationBearer |> AuthenticationBearer.value
                    Request = command.Request |> Request.value

                    Reactor = command.Reactor |> Reactor.serialize
                    Requestor = command.Requestor |> Requestor.serialize
                    ReplyTo = command.ReplyTo |> ReplyTo.serialize

                    MetaData = metaData
                    Data = data
                }
            }

[<RequireQualifiedAccess>]
module CommandDto =
    let serialize (serialize: obj -> string) = function
        | Synchronous command -> serialize command
        | Asynchronous command -> serialize command

type DtoError<'MetaData, 'Data> =
    | SpecificEventError of message: string * Command<'MetaData, 'Data>

[<RequireQualifiedAccess>]
module DtoError =
    let format = function
        | SpecificEventError (message, command) -> sprintf "Error %A for command:\n%A" message command

type Serialize<'Command, 'MetaData, 'Data, 'CommandDto> = 'Command -> Result<'CommandDto, DtoError<'MetaData, 'Data>>
