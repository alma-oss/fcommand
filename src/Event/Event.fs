namespace Alma.Command.Event

open Alma.Kafka

//
// Dto
//

type DtoError<'KeyData, 'MetaData, 'DomainData> =
    | WrongEventType of expectedEvent: string * Event<'KeyData, 'MetaData, 'DomainData>
    | MissingResourceData of Event<'KeyData, 'MetaData, 'DomainData>
    | MissingProcessedMetaData of Event<'KeyData, 'MetaData, 'DomainData>
    | SpecificEventError of message: string * Event<'KeyData, 'MetaData, 'DomainData>

[<RequireQualifiedAccess>]
module DtoError =
    let assertEventType expectedType (event: Event<_, _, _>) =
        if event.Event <> EventName expectedType
        then Error (WrongEventType (expectedType, event))
        else Ok ()

    let format = function
        | WrongEventType (expectedType, event) -> sprintf "Event has wrong event type. Expected %s but %A given." expectedType event.Event
        | MissingResourceData event -> sprintf "Event has no resource\n%A" event
        | MissingProcessedMetaData event -> sprintf "Event has no processed data in meta data\n%A" event
        | SpecificEventError (message, event) -> sprintf "%s\n%A" message event

type SerializeEventDto = obj -> string

type SerializeEvent<'Event, 'KeyData, 'MetaData, 'DomainData> = SerializeEventDto -> 'Event -> Result<string, DtoError<'KeyData, 'MetaData, 'DomainData>>
type SerializeEventOrFail<'Event> = SerializeEventDto -> 'Event -> string

//
// Transformation
//

module Transform =
    let toInternal metaData (event: Alma.Kafka.Event<_, _, _>): Alma.Kafka.Event<_, _, _> =
        {
            Schema = event.Schema
            Id = event.Id
            CorrelationId = event.CorrelationId
            CausationId = event.CausationId
            Timestamp = event.Timestamp
            Event = event.Event
            Domain = event.Domain
            Context = event.Context
            Purpose = event.Purpose
            Version = event.Version
            Zone = event.Zone
            Bucket = event.Bucket
            Resource = None
            MetaData = metaData
            KeyData = event.KeyData
            DomainData = event.DomainData
        }
