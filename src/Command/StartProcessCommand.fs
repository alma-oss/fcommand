namespace Lmc.Command

[<RequireQualifiedAccess>]
module StartProcess =
    open System
    open Lmc.Command
    open Lmc.ServiceIdentification
    open Lmc.Serializer
    open Lmc.ErrorHandling

    let private request = Request "start_process"

    type ProcessName = ProcessName of string

    [<RequireQualifiedAccess>]
    module ProcessName =
        let value (ProcessName value) = value

    [<RequireQualifiedAccess>]
    type ProcessVariable =
        | String of string * DataItem<string>
        | Integer of string * DataItem<int>
        | Float of string * DataItem<float>
        | Bool of string * DataItem<bool>
        | Object of string * DataItem<obj>

    type Command = private Command of ProcessName * Command<MetaData, CommandData>

    and CommandData = {
        Process: DataItem<ProcessName>
        ProcessVariables: DataItem<ProcessVariable list>
    }

    [<RequireQualifiedAccess>]
    module Command =
        let internal command (Command (_, command)) = command

        let private createCommand commandId processName requestor reactor authentication ttl replyTo (correlationId, causationId) processVariables =
            let now = DateTime.Now

            Command (processName, {
                Schema = 1
                Id = commandId
                CorrelationId = correlationId
                CausationId = causationId
                Timestamp = now |> Serialize.dateTime

                TimeToLive = ttl
                AuthenticationBearer = authentication
                Request = request

                Reactor = reactor
                Requestor = requestor
                ReplyTo = replyTo

                MetaData = OnlyCreatedAt (CreatedAt now)
                Data = Data {
                    Process = (processName, "string") |> DataItem.createWithType
                    ProcessVariables = (processVariables, "json") |> DataItem.createWithType
                }
            })

        let createInCorrelation processName requestor reactor authentication ttl replyTo (correlationId, causationId) processVariables =
            createCommand (CommandId.create()) processName requestor reactor authentication ttl replyTo (correlationId, causationId) processVariables

        let create processName requestor reactor authentication ttl replyTo processVariables =
            let commandId = CommandId.create()
            let correlation = (
                CorrelationId.fromCommandId commandId,
                CausationId.fromCommandId commandId
            )

            createCommand (CommandId.create()) processName requestor reactor authentication ttl replyTo correlation processVariables
    //
    // Serialize DTO
    //

    type DtoDataItems = Map<string, DataItemDto<obj>>

    type DataDto = {
        Process: DataItemDto<string>
        ProcessVariables: DataItemDto<DtoDataItems>
    }

    type CommandDto = CommandDto<MetaDataDto.OnlyCreatedAt, DataDto>

    [<RequireQualifiedAccess>]
    module private Dto =
        let private serializeCommandData serializeObj (data: CommandData) = result {
            return {
                Process = data.Process |> DataItemDto.serialize ProcessName.value
                ProcessVariables =
                    data.ProcessVariables
                    |> DataItemDto.serialize (
                        List.map (function
                            | ProcessVariable.String (name, value) -> name, value |> DataItemDto.serializeScalar
                            | ProcessVariable.Integer (name, value) -> name, value |> DataItemDto.serializeScalar
                            | ProcessVariable.Float (name, value) -> name, value |> DataItemDto.serializeScalar
                            | ProcessVariable.Bool (name, value) -> name, value |> DataItemDto.serializeScalar
                            | ProcessVariable.Object (name, value) -> name, value |> DataItemDto.serialize serializeObj
                        )
                        >> Map.ofList
                    )
            }
        }

        let fromCommand serializeObj (Command (processName, command)) =
            command
            |> Command.toDto
                MetaDataDto.serialize
                (serializeCommandData serializeObj)

    // Public DTO functions

    let serialize serializeObj: Serialize<Command, MetaData, CommandData, CommandDto> =
        Dto.fromCommand serializeObj
