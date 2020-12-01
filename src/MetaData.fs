namespace Lmc.Command

[<AutoOpen>]
module MetaData =
    open System

    type NotParsed = NotParsed
    type CreatedAt = CreatedAt of DateTime

    [<RequireQualifiedAccess>]
    module CreatedAt =
        let value (CreatedAt date) = date
        let now () = CreatedAt (DateTime.Now)

    type MetaData =
        | OnlyCreatedAt of CreatedAt

    [<RequireQualifiedAccess>]
    type MetaDataParseError =
        | InvalidSchema of data: string * message: string

    [<RequireQualifiedAccess>]
    module MetaDataParseError =
        let format = function
            | MetaDataParseError.InvalidSchema (data, message) -> sprintf "MetaData are invalid - %s.\n%A" message data

    [<RequireQualifiedAccess>]
    module private Parser =
        open FSharp.Data

        type private MetaDataSchema = JsonProvider<"src/schema/metaData.json", SampleIsList = true>

        let parse (RawData metaDataJsonValue) =
            try
                let parsedMetaData = metaDataJsonValue.ToString() |> MetaDataSchema.Parse
                let createdAt = CreatedAt parsedMetaData.CreatedAt.DateTime

                Ok (OnlyCreatedAt createdAt)
            with
            | error -> Error (MetaDataParseError.InvalidSchema (metaDataJsonValue.ToString(), error.Message))

    [<RequireQualifiedAccess>]
    module MetaData =
        let createdAt = function
            | OnlyCreatedAt createdAt -> createdAt

        let parse = Parser.parse

    [<RequireQualifiedAccess>]
    module MetaDataDto =
        open Lmc.Serializer

        type OnlyCreatedAt = {
            CreatedAt: string
        }

        let fromCreatedAt (CreatedAt createdAt) =
            {
                CreatedAt = createdAt |> Serialize.dateTime
            }

        let serialize = function
            | OnlyCreatedAt createdAt -> createdAt |> fromCreatedAt |> Ok
