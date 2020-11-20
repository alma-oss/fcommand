namespace Lmc.Command

open System
open Lmc.ServiceIdentification

// Simple types

type RawData = RawData of FSharp.Data.JsonValue

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

    let mapt newType f item =
        {
            Value = item.Value |> f
            Type = newType
        }

    let map f item = item |> mapt item.Type f

type Data<'CommandData> = Data of 'CommandData
