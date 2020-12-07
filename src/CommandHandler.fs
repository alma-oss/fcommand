namespace Lmc.Command

open System
open Lmc.ErrorHandling
open Lmc.ServiceIdentification

//
// Errors
//

type CommandHandleError<'Error> =
    | InvalidHandler of handle: Request * given: Request
    | InvalidTimestamp of string
    | Timeout
    | InvalidReactor of Reactor * Box
    | HandleSpecificError of 'Error

[<RequireQualifiedAccess>]
module CommandHandleError =
    let format formatSpecificError = function
        | InvalidHandler (expected, given) -> sprintf "[Command Handler] Command Handler<%s> is invalid for a given Command<%s>." (expected |> Request.value) (given |> Request.value)
        | InvalidTimestamp timestamp -> sprintf "[Command Handler] Command has an invalid timestamp %A." timestamp
        | Timeout -> "[Command Handler] Handling a command timeouted."
        | InvalidReactor (Reactor reactorPattern, reactor) -> sprintf "[Command Handler] Invalid Reactor %A called for a command, expecting %A." reactor reactorPattern
        | HandleSpecificError e -> e |> formatSpecificError |> sprintf "[Command Handler] Fail on a command specific error: %s"

//
// Command Handler types
//

type GetCommand<'Command, 'MetaData, 'CommandData> = 'Command -> Command<'MetaData, 'CommandData>

type CommandHandler<'Command, 'MetaData, 'CommandData, 'Error> = private {
    Handle: Request
    GetCommand: 'Command -> Command<'MetaData, 'CommandData>
    Handler: Command<'MetaData, 'CommandData> -> AsyncResult<unit, CommandHandleError<'Error>>
}

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
        // AuthenticationBearer: Validation // todo<later>
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

    let canHandle (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'Error>) (command: 'Command) =
        let common = command |> handler.GetCommand |> Command.toCommon
        common.Request = handler.Handle

    let private validate validations (currentReactor: Box) command = asyncResult {
        let common = command |> Command.toCommon

        if validations.TimeToLive = Validation.Validate then
            let! timestamp =
                match common.Timestamp |> DateTimeOffset.TryParse with
                | true, timestamp -> AsyncResult.ofSuccess timestamp
                | _ -> AsyncResult.ofError (InvalidTimestamp common.Timestamp)

            let ttl = common.TimeToLive |> TimeToLive.value |> float
            let validTo = timestamp.AddMilliseconds(ttl)

            let now = DateTimeOffset.Now |> Serialize.dateTimeOffset |> DateTimeOffset.Parse

            if validTo <= now then
                return! AsyncResult.ofError Timeout

        if validations.Reactor = Validation.Validate then
            let (Reactor reactorPattern) = common.Reactor
            if reactorPattern |> BoxPattern.isMatching currentReactor |> not then
                return! AsyncResult.ofError (InvalidReactor (common.Reactor, currentReactor))

        // todo<later> validate AuthorizationBearer

        return ()
    }

    let handleWith validations reactor (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'Error>) (command: 'Command) = asyncResult {
        if command |> canHandle handler |> not then
            return! AsyncResult.ofError (InvalidHandler (handler.Handle, (command |> handler.GetCommand |> Command.toCommon).Request))

        let command = command |> handler.GetCommand

        do! command |> validate validations reactor

        return! command |> handler.Handler
    }

    let handle reactor (handler: CommandHandler<'Command, 'MetaData, 'CommandData, 'Error>) (command: 'Command) =
        handleWith defaultValidations reactor handler command
