namespace Alma.Command

[<RequireQualifiedAccess>]
module internal EventType =
    open Alma.Kafka

    let assertSame = function
        | expectedEvent, (EventName eventName) when expectedEvent <> eventName -> Error ()
        | _ -> Ok ()
