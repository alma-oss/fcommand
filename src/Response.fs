namespace Lmc.Command

open System
open System.Net
open Lmc.ServiceIdentification
open Lmc.Serializer
open Lmc.ErrorHandling

//
// Common types
//

type ResponseId = ResponseId of Guid

[<RequireQualifiedAccess>]
module ResponseId =
    let value (ResponseId id) = id

type StatusCode = StatusCode of HttpStatusCode

[<RequireQualifiedAccess>]
module StatusCode =
    let value (StatusCode status) = int status

//
// Errors
//

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

//
// Command Response
//

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

type NoMetaData = NoMetaData

type CommandResponseError<'ResponseData> =
    | ParseError of string * exn
    | InvalidReactor
    | InvalidRequestor
    | ErrorResponse of CommandResponse<NoMetaData, 'ResponseData>

//
// Command Response DTO
//

type ResponseErrorDto = {
    Status: int
    Title: string
    Detail: string
}

type CommandResponseDto<'MetaDataDto, 'ResponseDataDto> = {
    Schema: int
    Id: Guid
    CorrelationId: Guid
    CausationId: Guid
    Timestamp: string

    Reactor: ReactorDto
    Requestor: RequestorDto

    MetaData: 'MetaDataDto
    Data: 'ResponseDataDto

    ResponseTo: string
    Response: int
    Errors: ResponseErrorDto list
}

//
// Command Response module
//

[<RequireQualifiedAccess>]
module CommandResponse =
    open FSharp.Data

    let create correlationId causationId timestamp reactor requestor responseTo response errors data: CommandResponse<_, _> =
        {
            Schema = 1
            Id = Guid.NewGuid() |> ResponseId
            CorrelationId = correlationId
            CausationId = causationId
            Timestamp = timestamp |> Serialize.dateTime

            Reactor = reactor
            Requestor = requestor

            MetaData = GenericMetaData.ofList [
                "created_at", (DateTime.Now |> Serialize.dateTime)
            ]
            Data = data

            ResponseTo = responseTo
            Response = response
            Errors = errors
        }

    type NotParsed = NotParsed

    type private CommandResponseSchema = JsonProvider<"src/schema/response.json", SampleIsList = true>

    let private parseHttpStatusCode (code: int): HttpStatusCode = enum code

    let ignoreMetadata (response: CommandResponse<_, _>): CommandResponse<_, _> =
        {
            Schema = response.Schema
            Id = response.Id
            CorrelationId = response.CorrelationId
            CausationId = response.CausationId
            Timestamp = response.Timestamp

            Reactor = response.Reactor
            Requestor = response.Requestor

            MetaData = NoMetaData
            Data = response.Data

            ResponseTo = response.ResponseTo
            Response = response.Response
            Errors = response.Errors
        }

    let parse (parseResponseMetaData: RawData -> GenericMetaData) parseData serializedResponse = result {
        try
            let rawResponse =
                serializedResponse
                |> CommandResponseSchema.Parse

            let data = rawResponse.Data.Attributes

            let! reactor =
                Box.createFromStrings(
                    data.Reactor.Domain,
                    data.Reactor.Context,
                    data.Reactor.Purpose,
                    data.Reactor.Version,
                    data.Reactor.Zone,
                    data.Reactor.Bucket
                )
                |> Result.ofOption InvalidReactor
                |> Result.map ReactorResponse

            let! requestor =
                Box.createFromStrings (
                    data.Requestor.Domain,
                    data.Requestor.Context,
                    data.Requestor.Purpose,
                    data.Requestor.Version,
                    data.Requestor.Zone,
                    data.Requestor.Bucket
                )
                |> Result.ofOption InvalidRequestor
                |> Result.map Requestor

            let response: CommandResponse<GenericMetaData, 'ResponseData> =
                {
                    Schema = data.Schema
                    Id = ResponseId data.Id
                    CorrelationId = CorrelationId data.CorrelationId
                    CausationId = CausationId data.CausationId
                    Timestamp = data.Timestamp |> Serialize.dateTimeOffset

                    Reactor = reactor
                    Requestor = requestor

                    MetaData = RawData data.MetaData.JsonValue |> parseResponseMetaData
                    Data = RawData data.Data.JsonValue |> parseData

                    ResponseTo = data.ResponseTo
                    Response = StatusCode (parseHttpStatusCode data.Response)
                    Errors =
                        data.Errors
                        |> Seq.map (fun e ->
                            let error: ResponseError = {
                                Detail = e.Detail
                                Status = StatusCode (parseHttpStatusCode e.Status)
                                Title =
                                    match e.Title with
                                    | Some title -> title
                                    | _ -> ""
                            }
                            error
                        )
                        |> Seq.toList
                }

            return!
                match response.Response, response.Errors with
                | (StatusCode code), [] when (int code) < 400 -> Ok response
                | _ -> Error (ErrorResponse (response |> ignoreMetadata))
        with
        | e ->
            return! Error (ParseError (serializedResponse, e))
    }

    let toDto metaData data (response: CommandResponse<_, _>): CommandResponseDto<_, _> =
        let (ReactorResponse reactor) = response.Reactor
        let (Requestor requestor) = response.Requestor

        {
            Schema = response.Schema
            Id = response.Id |> ResponseId.value
            CorrelationId = response.CorrelationId |> CorrelationId.value
            CausationId = response.CausationId |> CausationId.value
            Timestamp = response.Timestamp

            Reactor = {
                Domain = reactor.Domain |> Domain.value
                Context = reactor.Context |> Context.value
                Purpose = reactor.Purpose |> Purpose.value
                Version = reactor.Version |> Version.value
                Zone = reactor.Zone |> Zone.value
                Bucket = reactor.Bucket |> Bucket.value
            }
            Requestor = {
                Domain = requestor.Domain |> Domain.value
                Context = requestor.Context |> Context.value
                Purpose = requestor.Purpose |> Purpose.value
                Version = requestor.Version |> Version.value
                Zone = requestor.Zone |> Zone.value
                Bucket = requestor.Bucket |> Bucket.value
            }

            MetaData = response.MetaData |> metaData
            Data = response.Data |> data

            ResponseTo = response.ResponseTo
            Response = response.Response |> StatusCode.value
            Errors =
                response.Errors
                |> List.map (fun e ->
                    {
                        Status = e.Status |> StatusCode.value
                        Title = e.Title
                        Detail  = e.Detail
                    }
                )
        }
