namespace Lmc.Command

open System
open System.Net
open Lmc.ErrorHandling
open Lmc.ServiceIdentification

//
// Errors
//

type CommandHandleError<'Error> =
    | InvalidCommand of ResponseErrorDto list
    | InvalidHandler of handle: Request * given: Request
    | InvalidTimestamp of string
    | Timeout
    | InvalidReactor of Reactor * Box
    | HandleSpecificError of 'Error

[<RequireQualifiedAccess>]
module CommandHandleError =
    let format formatSpecificError = function
        | InvalidCommand errors -> sprintf "[Command Handler] Invalid command given, validation failed on:\n - %s" (errors |> List.map (fun { Title = title } -> title) |> String.concat "\n - ")
        | InvalidHandler (expected, given) -> sprintf "[Command Handler] Command Handler<%s> is invalid for a given Command<%s>." (expected |> Request.value) (given |> Request.value)
        | InvalidTimestamp timestamp -> sprintf "[Command Handler] Command has an invalid timestamp %A." timestamp
        | Timeout -> "[Command Handler] Handling a command timeouted."
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
    | AsynchronousCommandStarted
    | AsynchronousCommandNotStarted of CommandHandleError<'Error> list
    | SynchronousCommandResponse of CommandResponse<'MetaData, 'ResponseData>

[<RequireQualifiedAccess>]
module CommandHandler =
    open Lmc.Serializer

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
            let validTo = timestamp.AddMilliseconds(ttl)

            let now = DateTimeOffset.Now |> Serialize.dateTimeOffset |> DateTimeOffset.Parse

            if validTo <= now then
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
            Status = StatusCode HttpStatusCode.BadRequest
            Detail = error |> CommandHandleError.format formatError
            Title =
                match error with
                | InvalidCommand _ -> "CommandHandler.InvalidCommand"
                | InvalidHandler _ -> "CommandHandler.InvalidHandler"
                | InvalidTimestamp _ -> "CommandHandler.InvalidTimestamp"
                | Timeout -> "CommandHandler.Timeout"
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

    let private persistAsyncCommandResponse persistAsyncResponse (asyncCommand: AsynchronousCommand<_, _>) handleAsyncCommand =
        async {
            let! commandResponse = handleAsyncCommand

            do! commandResponse |> persistAsyncResponse asyncCommand.Id asyncCommand.ReplyTo
        }
        |> Async.Catch
        |> Async.map (Result.ofChoice >> Result.teeError (eprintfn "[CommandHandler][Async] %A") >> ignore)
        |> Async.Start

        AsynchronousCommandStarted

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
                | Command.Synchronous _ ->
                    async {
                        let! responseData = command |> handler.Handler

                        return
                            responseData
                            |> createCommandResponse reactor formatError specificErrorTitle (command |> Command.toCommon) "http"
                            |> SynchronousCommandResponse
                    }
                    |> Async.RunSynchronously

                | Command.Asynchronous asyncCommand ->
                    async {
                        let! handleResult = command |> handler.Handler

                        return
                            handleResult
                            |> createCommandResponse reactor formatError specificErrorTitle (command |> Command.toCommon) asyncCommand.ReplyTo.Type
                    }
                    |> persistAsyncCommandResponse persistAsyncResponse asyncCommand

            | Error validationErrors ->
                match command |> handler.GetCommand with
                | Command.Synchronous _ as command ->
                    validationErrors
                    |> List.map (formatResponseError formatError specificErrorTitle)
                    |> createResponse (command |> Command.toCommon) reactor "http" (StatusCode HttpStatusCode.UnprocessableEntity) None
                    |> SynchronousCommandResponse

                | Command.Asynchronous _ ->
                    validationErrors
                    |> AsynchronousCommandNotStarted

    let handle customValidation reactor formatError specificErrorTitle persistAsyncResponse (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'ResponseData, 'Error>) (command: 'Command) =
        handleWith defaultValidations customValidation reactor formatError specificErrorTitle persistAsyncResponse handler command
