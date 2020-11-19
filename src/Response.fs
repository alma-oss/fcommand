namespace Lmc.Command

open System
open System.Net
open Lmc.ServiceIdentification

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

type CommandResponseError<'MetaData, 'ResponseData> =
    | ParseError of string * exn
    | InvalidReactor
    | InvalidRequestor
    | ErrorResponse of CommandResponse<'MetaData, 'ResponseData>

[<RequireQualifiedAccess>]
module CommandResponse =
    open FSharp.Data

    type NotParsed = NotParsed

    type private CommandResponseSchema = JsonProvider<"src/schema/response.json", SampleIsList = true>

    let parseHttpStatusCode (code: int): HttpStatusCode = enum code

    let parse serializedResponse = result {
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

            let response: CommandResponse<NotParsed, NotParsed> =
                {
                    Schema = data.Schema
                    Id = ResponseId data.Id
                    CorrelationId = CorrelationId data.CorrelationId
                    CausationId = CausationId data.CausationId
                    Timestamp = data.Timestamp |> CommonSerializer.formatDateTimeOffset

                    Reactor = reactor
                    Requestor = requestor

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
