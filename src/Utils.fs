namespace Lmc.Command

module CommonSerializer =
    open System

    let formatDateTime (dateTime: DateTime) =
        dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")

    let formatDateTimeOffset (dateTime: DateTimeOffset) =
        dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")

    type internal StringOrNull = string option -> string

    let internal stringOrNull: StringOrNull = function
        | Some string -> string
        | _ -> null
