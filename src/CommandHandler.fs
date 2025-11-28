namespace Alma.Command

open System
open System.Net
open Feather.ErrorHandling
open Alma.ServiceIdentification

//
// Errors
//

type CommandHandleError<'Error> =
    | InvalidCommand of ResponseErrorDto list
    | InvalidHandler of handle: Request * given: Request
    | InvalidTimestamp of string
    | Timeout
    | Unauthorized
    | InvalidReactor of Reactor * Box
    | HandleSpecificError of 'Error

[<RequireQualifiedAccess>]
module CommandHandleError =
    let format formatSpecificError = function
        | InvalidCommand errors -> sprintf "[Command Handler] Invalid command given, validation failed on:\n - %s" (errors |> List.map (fun { Title = title } -> title) |> String.concat "\n - ")
        | InvalidHandler (expected, given) -> sprintf "[Command Handler] Command Handler<%s> is invalid for a given Command<%s>." (expected |> Request.value) (given |> Request.value)
        | InvalidTimestamp timestamp -> sprintf "[Command Handler] Command has an invalid timestamp %A." timestamp
        | Timeout -> "[Command Handler] Handling a command timeouted."
        | Unauthorized -> "[Command Handler] Handling a command is unauthorized by a given authorization bearer in the command."
        | InvalidReactor (Reactor reactorPattern, reactor) -> sprintf "[Command Handler] Invalid Reactor %A called for a command, expecting %A." reactor reactorPattern
        | HandleSpecificError e -> e |> formatSpecificError |> sprintf "[Command Handler] Fail on a command specific error: %s"

//
// Command Handler types
//

type GetCommand<'Command, 'MetaData, 'CommandData> = 'Command -> Command<'MetaData, 'CommandData>

type CommandHandler<'Command, 'MetaData, 'CommandData, 'ResponseData, 'Error> = private {
    Handle: Request
    GetCommand: 'Command -> Command<'MetaData, 'CommandData>
    Handler: Command<'MetaData, 'CommandData> -> AsyncResult<Data<'ResponseData> option, CommandHandleError<'Error>>
}

type CommandHandleResult<'MetaData, 'ResponseData, 'Error> =
    | CommandStarted
    | CommandNotStarted of CommandHandleError<'Error> list
    | CommandResponse of CommandResponse<'MetaData, 'ResponseData>

[<RequireQualifiedAccess>]
module CommandHandler =
    open Alma.Serializer

    [<RequireQualifiedAccess>]
    type Validation =
        | Ignore
        | Validate

    type Validations = {
        TimeToLive: Validation
        Reactor: Validation
    }

    let defaultValidations = {
        TimeToLive = Validation.Validate
        Reactor = Validation.Validate
    }

    let create request getCommand handle =
        {
            Handle = request
            GetCommand = getCommand
            Handler = handle
        }

    let canHandle (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'ResponseData, 'Error>) (command: 'Command) =
        let common = command |> handler.GetCommand |> Command.toCommon
        common.Request = handler.Handle

    let private validate validations (currentReactor: Box) command = result {
        let common = command |> Command.toCommon

        if validations.TimeToLive = Validation.Validate then
            let! timestamp =
                match common.Timestamp |> DateTimeOffset.TryParse with
                | true, timestamp -> Ok timestamp
                | _ -> Error (InvalidTimestamp common.Timestamp)

            let ttl = common.TimeToLive |> TimeToLive.value |> float

            if ttl > 0. then
                let validFrom = timestamp
                let validTo = validFrom.AddMilliseconds(ttl)

                let now = DateTimeOffset.Now |> Serialize.dateTimeOffset |> DateTimeOffset.Parse
                let isValid = validFrom <= now && now <= validTo

                if not isValid then
                    return! Error Timeout

        if validations.Reactor = Validation.Validate then
            let (Reactor reactorPattern) = common.Reactor
            if reactorPattern |> BoxPattern.isMatching currentReactor |> not then
                return! Error (InvalidReactor (common.Reactor, currentReactor))

        return ()
    }

    let private createResponse (command: CommonCommandData) reactor responseTo status data errors =
        CommandResponse.create
            command.CorrelationId
            (CausationId.fromCommandId command.Id)
            DateTime.Now
            (ReactorResponse reactor)
            command.Requestor
            responseTo
            status
            errors
            data

    let private formatResponseError formatError specificErrorTitle error: ResponseError =
        {
            Status =
                match error with
                | Timeout -> StatusCode HttpStatusCode.RequestTimeout
                | Unauthorized -> StatusCode HttpStatusCode.Unauthorized
                | _ -> StatusCode HttpStatusCode.BadRequest
            Detail =
                error |> CommandHandleError.format formatError
            Title =
                match error with
                | InvalidCommand _ -> "CommandHandler.InvalidCommand"
                | InvalidHandler _ -> "CommandHandler.InvalidHandler"
                | InvalidTimestamp _ -> "CommandHandler.InvalidTimestamp"
                | Timeout -> "CommandHandler.Timeout"
                | Unauthorized -> "CommandHandler.Unauthorized"
                | InvalidReactor _ -> "CommandHandler.InvalidReactor"
                | HandleSpecificError specific -> specific |> specificErrorTitle |> sprintf "CommandHandler.HandleSpecificError.%s"
        }

    let private createCommandResponse reactor (formatError: 'Error -> string) (specificErrorTitle: 'Error -> string) (command: CommonCommandData) responseTo response =
        let createResponse = createResponse command reactor responseTo

        match response with
        | Ok data -> createResponse (StatusCode HttpStatusCode.Created) data []
        | Error error ->
            createResponse (StatusCode HttpStatusCode.BadRequest) None [
                error |> formatResponseError formatError specificErrorTitle
            ]

    open Result.Operators

    let private persistCommandResponse persistResponse (command: Command<_, _>) handleCommand =
        async {
            let! commandResponse = handleCommand

            do! commandResponse |> persistResponse command.Id command.ReplyTo
        }
        |> AsyncResult.ofAsyncCatch id
        |> Async.map (Result.teeError (eprintfn "[CommandHandler][Async] %A") >> ignore)
        |> Async.Start

        CommandStarted

    let handleWith
        validations
        customValidation
        (formatError: 'Error -> string)
        (specificErrorTitle: 'Error -> string)
        (persistAsyncResponse: CommandId -> ReplyTo -> CommandResponse<GenericMetaData, 'ResponseData> -> Async<unit>)
        reactor
        (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'ResponseData, 'Error>)
        (command: 'Command): CommandHandleResult<GenericMetaData, 'ResponseData, 'Error> =

            let validate = result {
                if command |> canHandle handler |> not then
                    return! Error [ (InvalidHandler (handler.Handle, (command |> handler.GetCommand |> Command.toCommon).Request)) ]

                let command = command |> handler.GetCommand

                do!
                    [
                        command |> validate validations reactor
                        command |> customValidation <@> InvalidCommand
                    ]
                    |> Validation.ofResults
                    <!> ignore

                return command
            }

            match validate with
            | Ok command ->
                match command with
                | { ReplyTo = ReplyTo.IsHttpCallerConnection } ->
                    async {
                        let! responseData = command |> handler.Handler

                        return
                            responseData
                            |> createCommandResponse reactor formatError specificErrorTitle (command |> Command.toCommon) command.ReplyTo.Type
                            |> CommandResponse
                    }
                    |> Async.RunSynchronously

                | command ->
                    async {
                        let! handleResult = command |> handler.Handler

                        return
                            handleResult
                            |> createCommandResponse reactor formatError specificErrorTitle (command |> Command.toCommon) command.ReplyTo.Type
                    }
                    |> persistCommandResponse persistAsyncResponse command

            | Error validationErrors ->
                match command |> handler.GetCommand with
                | { ReplyTo = ReplyTo.IsHttpCallerConnection } as command ->
                    validationErrors
                    |> List.map (formatResponseError formatError specificErrorTitle)
                    |> createResponse (command |> Command.toCommon) reactor command.ReplyTo.Type (StatusCode HttpStatusCode.UnprocessableEntity) None
                    |> CommandResponse

                | _ -> CommandNotStarted validationErrors

    let handle customValidation reactor formatError specificErrorTitle persistAsyncResponse (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'ResponseData, 'Error>) (command: 'Command) =
        handleWith defaultValidations customValidation reactor formatError specificErrorTitle persistAsyncResponse handler command
