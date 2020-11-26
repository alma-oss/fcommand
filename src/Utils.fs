namespace Lmc.Command

[<RequireQualifiedAccess>]
module internal EventType =
    open Lmc.Kafka

    let assertSame = function
        | expectedEvent, (EventName eventName) when expectedEvent <> eventName -> Error ()
        | _ -> Ok ()
