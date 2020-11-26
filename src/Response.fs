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

type StatusCode = StatusCode of HttpStatusCode

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

[<RequireQualifiedAccess>]
module CommandResponse =
    open FSharp.Data

    let create correlationId causationId timestamp reactor requestor responseTo response errors data  =
        {
            Schema = 1
            Id = Guid.NewGuid() |> ResponseId
            CorrelationId = correlationId
            CausationId = causationId
            Timestamp = timestamp |> Serialize.dateTime

            Reactor = reactor
            Requestor = requestor

            MetaData = CreatedAt.now()
            Data = data

            ResponseTo = responseTo
            Response = response
            Errors = errors
        }

    type NotParsed = NotParsed

    type private CommandResponseSchema = JsonProvider<"src/schema/response.json", SampleIsList = true>

    let private parseHttpStatusCode (code: int): HttpStatusCode = enum code

    let ignoreMetadata response =
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

    let parse parseData serializedResponse = result {
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

            let response: CommandResponse<NotParsed, 'ResponseData> =
                {
                    Schema = data.Schema
                    Id = ResponseId data.Id
                    CorrelationId = CorrelationId data.CorrelationId
                    CausationId = CausationId data.CausationId
                    Timestamp = data.Timestamp |> Serialize.dateTimeOffset

                    Reactor = reactor
                    Requestor = requestor

                    MetaData = NotParsed
                    Data = parseData (RawData data.Data.JsonValue)

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
                | _ -> Error (ErrorResponse (response |> ignoreMetadata))
        with
        | e ->
            return! Error (ParseError (serializedResponse, e))
    }
