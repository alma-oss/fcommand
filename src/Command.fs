namespace Lmc.Command

open System
open Lmc.ServiceIdentification
open Lmc.ErrorHandling

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
// Serialize Command
//

type SerializeCommand<'MetaData, 'Data, 'MetaDataDto, 'DataDto, 'Error> =
    ('MetaData -> Result<'MetaDataDto, 'Error>) -> ('Data -> Result<'DataDto, 'Error>)
        -> Command<'MetaData, 'Data>
        -> Result<CommandDto<'MetaDataDto, 'DataDto>, 'Error>

type CommandParseError =
    | UnsupportedSchema of int
    | RequestError of RequestError
    | InvalidReactor
    | InvalidRequestor
    | MissingData
    | Other of string

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

    open FSharp.Data
    open Lmc.Serializer
    open Lmc.ErrorHandling.Result.Operators

    type private CommandSchema = JsonProvider<"src/schema/command.json", SampleIsList = true>

    let parse parseMetaData parseData serializedCommand = result {
        try
            let rawCommand =
                serializedCommand
                |> CommandSchema.Parse

            if rawCommand.Schema <> 1 then
                return! Error (UnsupportedSchema rawCommand.Schema)

            let! request = Request.create rawCommand.Request <@> RequestError

            let! reactor =
                BoxPattern.createFromStrings (
                    rawCommand.Reactor.Domain,
                    rawCommand.Reactor.Context,
                    rawCommand.Reactor.Purpose,
                    rawCommand.Reactor.Version,
                    rawCommand.Reactor.Zone,
                    rawCommand.Reactor.Bucket
                )
                |> Result.ofOption InvalidReactor
                |> Result.map Reactor

            let! requestor =
                Box.createFromStrings (
                    rawCommand.Requestor.Domain,
                    rawCommand.Requestor.Context,
                    rawCommand.Requestor.Purpose,
                    rawCommand.Requestor.Version,
                    rawCommand.Requestor.Zone,
                    rawCommand.Requestor.Bucket
                )
                |> Result.ofOption InvalidRequestor
                |> Result.map Requestor

            let! metaData = rawCommand.MetaData.JsonValue |> RawData |> parseMetaData
            let! data =
                rawCommand.Data
                |> Result.ofOption MissingData
                |> Result.bind (fun data -> data.JsonValue |> RawData |> parseData)

            let id = CommandId rawCommand.Id
            let correlationId = CorrelationId rawCommand.CorrelationId
            let causationId = CausationId rawCommand.CausationId
            let timestamp = rawCommand.Timestamp |> Serialize.dateTimeOffset

            let ttl = rawCommand.Ttl |> TimeToLive.ofMiliSeconds

            let authenticationBearer =
                match rawCommand.AuthenticationBearer with
                | Some authentication -> AuthenticationBearer authentication
                | _ -> AuthenticationBearer.empty

            return
                match rawCommand.ReplyTo with
                | Some replyTo ->
                    Command.Asynchronous {
                        Schema = 1
                        Id = id
                        CorrelationId = correlationId
                        CausationId = causationId
                        Timestamp = timestamp

                        TimeToLive = ttl
                        AuthenticationBearer = authenticationBearer

                        Request = request

                        Reactor = reactor
                        Requestor = requestor
                        ReplyTo = {
                            Type = replyTo.Type
                            Identification = replyTo.Identification |> Option.defaultValue ""
                        }

                        MetaData = metaData
                        Data = data
                    }

                | _ ->
                    Command.Synchronous {
                        Schema = 1
                        Id = id
                        CorrelationId = correlationId
                        CausationId = causationId
                        Timestamp = timestamp

                        TimeToLive = ttl
                        AuthenticationBearer = authenticationBearer

                        Request = request

                        Reactor = reactor
                        Requestor = requestor

                        MetaData = metaData
                        Data = data
                    }
        with
        | e ->
            return! Error (Other e.Message)
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
