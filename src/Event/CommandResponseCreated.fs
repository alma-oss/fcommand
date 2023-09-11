namespace Alma.Command.Event

[<RequireQualifiedAccess>]
module CommandResponseCreated =
    open System
    open FSharp.Data
    open Alma.Kafka
    open Alma.ServiceIdentification
    open Alma.ErrorHandling
    open Alma.ErrorHandling.Result.Operators
    open Alma.Command

    type KafkaEvent<'KeyData, 'MetaData, 'DomainData> = Alma.Kafka.Event<'KeyData, 'MetaData, 'DomainData>
    module KafkaEvent = Alma.Kafka.Event

    //
    // Event types and constants
    //

    [<Literal>]
    let Name = "command_response_created"

    type KeyData = {
        CommandId: CommandId
    }

    type DomainData<'ResponseData> = {
        Response: CommandResponse<GenericMetaData, 'ResponseData>
    }

    type MetaData = {
        CreatedAt: DateTime
    }

    type NotParsed = NotParsed

    [<RequireQualifiedAccess>]
    module KeyData =
        let commandId: KeyData -> CommandId = fun { CommandId = commandId } -> commandId

    [<RequireQualifiedAccess>]
    module DomainData =
        let response: DomainData<'Response> -> _ = fun { Response = response } -> response

    type MetaDataParseError =
        | Invalid of exn

    [<RequireQualifiedAccess>]
    module MetaData =
        type private MetaDataSchema = JsonProvider<"src/schema/CommandResponseCreated/metaData.json", SampleIsList = true>

        let parse metaData =
            try
                let parsed = metaData |> RawData.toJson |> MetaDataSchema.Parse

                Ok {
                    CreatedAt = parsed.CreatedAt.DateTime
                }
            with e -> Error (Invalid e)

    type InternalEvent<'Response> = private InternalEvent of KafkaEvent<KeyData, MetaData, DomainData<'Response>>

    [<RequireQualifiedAccess>]
    module InternalEvent =
        let event (InternalEvent event) = event

    type InternalEventWithtoutMetaData<'Response> = private InternalEventWithtoutMetaData of KafkaEvent<KeyData, NotParsed, DomainData<'Response>>

    [<RequireQualifiedAccess>]
    module InternalEventWithtoutMetaData =
        let event (InternalEventWithtoutMetaData event) = event

    type Event<'Response> =
        | Complete of InternalEvent<'Response>
        | WithoutMetaData of InternalEventWithtoutMetaData<'Response>

    //
    // Parse event
    //

    [<RequireQualifiedAccess>]
    type ParseError<'ResponseData> =
        | InvalidEventType
        | CommandResponseError of CommandResponseError<'ResponseData>
        | MetaDataParseError of MetaDataParseError

    module private Parse =
        type private KeyDataSchema = JsonProvider<"src/schema/CommandResponseCreated/keyData.json", SampleIsList = true>
        type private DomainDataSchema = JsonProvider<"src/schema/CommandResponseCreated/domainData.json", SampleIsList = true>

        [<RequireQualifiedAccess>]
        type ParseType =
            | Complete
            | WithoutMetaData

        let parseEvent parseType parseResponseMetaData (parseResponseData: _ -> Data<'ResponseData> option) (rawEvent: Alma.Kafka.RawEvent) =
            result {
                do! EventType.assertSame (Name, rawEvent.Event) <@> (fun _ -> ParseError.InvalidEventType)

                let (Alma.Kafka.RawData keyDataJsonValue) = rawEvent.KeyData
                let parsedKeyData =
                    keyDataJsonValue.ToString()
                    |> KeyDataSchema.Parse

                let commandId = CommandId parsedKeyData.CommandId

                let keyData: KeyData =
                    {
                        CommandId = commandId
                    }

                let (Alma.Kafka.RawData domainDataJsonValue) = rawEvent.DomainData.Value
                let parsedDomainData =
                    domainDataJsonValue.ToString()
                    |> DomainDataSchema.Parse

                let! (response: CommandResponse<GenericMetaData, 'ResponseData>) =
                    parsedDomainData.Response.ToString()
                    |> CommandResponse.parse parseResponseMetaData parseResponseData
                    <@> ParseError.CommandResponseError

                let domainData: DomainData<'ResponseData> =
                    {
                        Response = response
                    }

                let createEvent (event: KafkaEvent<KeyData, 'MetaData, DomainData<'ResponseData>> -> _) (metaData: 'MetaData): Event<'ResponseData> =
                    event {
                        Schema = rawEvent.Schema
                        Id = rawEvent.Id
                        CorrelationId = rawEvent.CorrelationId
                        CausationId = rawEvent.CausationId
                        Timestamp = rawEvent.Timestamp
                        Event = rawEvent.Event
                        Domain = rawEvent.Domain
                        Context = rawEvent.Context
                        Purpose = rawEvent.Purpose
                        Version = rawEvent.Version
                        Zone = rawEvent.Zone
                        Bucket = rawEvent.Bucket
                        MetaData = metaData
                        Resource = rawEvent.Resource
                        KeyData = keyData
                        DomainData = domainData
                    }

                let! (event: Event<_>) =
                    match parseType with
                    | ParseType.WithoutMetaData ->
                        NotParsed
                        |> createEvent (InternalEventWithtoutMetaData >> WithoutMetaData)
                        |> Ok

                    | ParseType.Complete ->
                        rawEvent.MetaData.Value
                        |> MetaData.parse
                        <@> ParseError.MetaDataParseError
                        <!> createEvent (InternalEvent >> Complete)

                return event
            }

    // Public parse functions

    let parse parseData = Parse.parseEvent Parse.ParseType.Complete parseData
    let parseWithoutMetadata parseData = Parse.parseEvent Parse.ParseType.WithoutMetaData parseData

    //
    // Derivation event
    //

    module private Deriver =
        open Alma.Kafka
        open Alma.ServiceIdentification

        let deriveFromCommandResponse (deriver: Box) commandId (response: CommandResponse<GenericMetaData, 'ResponseData>) =
            let correlationId (Alma.Command.CorrelationId id) = Alma.Kafka.CorrelationId id
            let causationId (Alma.Command.CausationId id) = Alma.Kafka.CausationId id

            InternalEvent {
                Schema = 1
                Id = Guid.NewGuid() |> EventId
                CorrelationId = response.CorrelationId |> correlationId
                CausationId = response.CausationId |> causationId
                Timestamp = response.Timestamp
                Event = EventName Name
                Domain = deriver.Domain
                Context = deriver.Context
                Purpose = deriver.Purpose
                Version = deriver.Version
                Zone = deriver.Zone
                Bucket = deriver.Bucket
                MetaData = {
                    CreatedAt = DateTime.Now
                }
                Resource = None
                KeyData = {
                    CommandId = commandId
                }
                DomainData = {
                    Response = response
                }
            }

    let deriveFromCommandResponse deriver commandId: CommandResponse<_, 'ResponseData> -> Event<'ResponseData> =
        Deriver.deriveFromCommandResponse deriver commandId >> Complete

    //
    // Transformations of event
    //

    (* let toInternal: Transform<Event, InternalEvent> =
        fun processedBy -> function
            | Complete (InternalEvent event) -> event |> Transform.toInternal processedBy |> InternalEvent
            | WithoutMetaData (InternalEventWithtoutMetaData event) -> event |> Transform.toInternal processedBy |> InternalEvent

    let toPublic: Transform<Event, PublicEvent> =
        let publicDomainData _ = NoPublicDomainData

        fun processedBy -> function
            | Complete (InternalEvent event) -> event |> Transform.toPublic publicDomainData processedBy |> PublicEvent
            | WithoutMetaData (InternalEventWithtoutMetaData event) -> event |> Transform.toPublic publicDomainData processedBy |> PublicEvent
    *)

    //
    // Serialize DTO
    //

    type KeyDataDto = {
        CommandId: Guid
    }

    type DomainDataDto<'ResponseMetaDataDto, 'ResponseDataDto> = {
        Response: CommandResponseDto<'ResponseMetaDataDto, 'ResponseDataDto>
    }

    type EventDto<'ResponseMetaDataDto, 'ResponseDataDto> = Alma.Kafka.EventDto<Alma.Kafka.NoData, KeyDataDto, MetaDataDto.OnlyCreatedAt, DomainDataDto<'ResponseMetaDataDto, 'ResponseDataDto>>

    module private Dto =
        open Alma.Serializer

        let private serializeMetaData: MetaData -> Result<MetaDataDto.OnlyCreatedAt, _> = fun m ->
            Ok {
                CreatedAt = m.CreatedAt |> Serialize.dateTime
            }

        let private serializeKeyData: KeyData -> KeyDataDto = fun keyData ->
            {
                CommandId = keyData.CommandId |> CommandId.value
            }

        let private serializeDomainData serializeResponse: DomainData<_> -> DomainDataDto<_, _> = fun domainData ->
            {
                Response = domainData.Response |> serializeResponse
            }

        let toInternalDto serialize serializeResponse (event: KafkaEvent<KeyData, MetaData, DomainData<'Response>>) =
            event
            |> KafkaEvent.toDto
                (DtoError.assertEventType Name)
                Ok
                serializeMetaData
                (serializeKeyData >> Ok)
                (serializeDomainData serializeResponse >> Ok)
            <!> Alma.Kafka.EventDto.serialize serialize

    // Public Dto functions

    let serializeInternal serializeResponse: SerializeEvent<InternalEvent<'Response>, KeyData, MetaData, DomainData<'Response>> =
        fun serialize (InternalEvent event) ->
            event |> Dto.toInternalDto serialize serializeResponse

    let private failOnErrorWith (formatError: 'Error -> string) = function
        | Ok success -> success
        | Error error -> failwithf "%s" <| formatError error

    let serializeInternalOrFail serializeResponse: SerializeEventOrFail<InternalEvent<'Response>> =
        fun serialize -> serializeInternal serializeResponse serialize >> failOnErrorWith Event.DtoError.format

    [<RequireQualifiedAccess>]
    module Event =
        let toCommon: Event<_> -> Alma.Kafka.CommonEvent = function
            | Complete (InternalEvent event) -> event |> KafkaEvent.toCommon
            | WithoutMetaData (InternalEventWithtoutMetaData event) -> event |> KafkaEvent.toCommon

        let box event = event |> toCommon |> Alma.Kafka.CommonEvent.box

        let keyData: Event<_> -> KeyData = function
            | Complete (InternalEvent { KeyData = keyData }) -> keyData
            | WithoutMetaData (InternalEventWithtoutMetaData { KeyData = keyData }) -> keyData

        let domainData: Event<'Response> -> DomainData<'Response> = function
            | Complete (InternalEvent { DomainData = domainData }) -> domainData
            | WithoutMetaData (InternalEventWithtoutMetaData { DomainData = domainData }) -> domainData

        let private toInternal = function
            | Complete (InternalEvent event) -> event |> Transform.toInternal event.MetaData |> InternalEvent
            | WithoutMetaData (InternalEventWithtoutMetaData event) -> event |> Transform.toInternal { CreatedAt = DateTime.Now } |> InternalEvent

        let serialize serialize serializeResponse event =
            event |> toInternal |> serializeInternal serializeResponse serialize

        let key event =
            let (InternalEvent event) = event |> toInternal

            MessageKey.Delimited [
                event.Zone |> Zone.value
                event.Bucket |> Bucket.value
            ]
