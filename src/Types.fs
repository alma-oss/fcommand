namespace Lmc.Command

open System
open Lmc.ServiceIdentification
open Lmc.ErrorHandling

// Simple types

type CommandId = CommandId of Guid

[<RequireQualifiedAccess>]
module CommandId =
    let value (CommandId id) = id

    let tryParse (id: string) =
        match id |> Guid.TryParse with
        | true, uuid -> Some (CommandId uuid)
        | _ -> None

    let create () =
        Guid.NewGuid() |> CommandId

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

    let serializeResult serialize (item: DataItem<_>): Result<DataItemDto<_>, _> = result {
        let! value = item.Value |> serialize

        return {
            Value = value
            Type = item.Type
        }
    }

    let internal serializeScalar item = item |> serialize (fun a -> a :> obj)

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
    let [<Literal>] TypeHttp = "http"
    let [<Literal>] IdentificationHttp = "caller_connection"

    let HttpCallerConnection: ReplyTo = {
        Type = TypeHttp
        Identification = IdentificationHttp
    }

    let (|IsHttpCallerConnection|_|) = function
        | replyTo when replyTo = HttpCallerConnection -> Some IsHttpCallerConnection
        | _ -> None

    let internal serialize (replyTo: ReplyTo): ReplyToDto =
        {
            Type = replyTo.Type
            Identification = replyTo.Identification
        }

[<RequireQualifiedAccess>]
module Data =
    let data (Data data) = data

//
// Raw Data
//

type RawData = RawData of FSharp.Data.JsonValue

[<RequireQualifiedAccess>]
module RawData =
    open FSharp.Data

    let value (RawData value) = value

    let (|Itemi|_|) (index, key) (RawData data) =
        match data with
        | JsonValue.Record (values) ->
            try
                match values.[index] with
                | k, v when k = key -> Some (Itemi v)
                | _ -> None
            with _ -> None
        | _ -> None

    let (|Item|_|) key (RawData data) =
        match data with
        | JsonValue.Record (values) ->
            values
            |> Seq.tryPick (function
                | (name, value) when name = key -> Item (RawData value) |> Some
                | _ -> None
            )
        | _ -> None

    let (|Json|_|) (RawData data) = Some (data.ToString())

    let (|DataItem|_|) data: DataItem<_> option = maybe {
        let! v =
            match data with
            | Item "value" v -> Some (v |> value)
            | _ -> None

        let! t =
            match data with
            | Item "type" t -> Some (t |> value)
            | _ -> None

        return {
            Value = v.AsString()
            Type = t.AsString()
        }
    }

    let (|DataItemRaw|_|) data: DataItem<RawData> option = maybe {
        let! v =
            match data with
            | Item "value" v -> Some v
            | _ -> None

        let! t =
            match data with
            | Item "type" t -> Some (t |> value)
            | _ -> None

        return {
            Value = v
            Type = t.AsString()
        }
    }

//
// Generic Meta Data
//

type GenericMetaData = GenericMetaData of Map<string, string>

[<RequireQualifiedAccess>]
module GenericMetaData =
    let ofList = Map.ofList >> GenericMetaData
