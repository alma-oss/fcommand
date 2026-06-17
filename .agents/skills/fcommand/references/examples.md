# Examples

All code for this skill lives here, ordered from simplest to a full workflow. Each example is self-contained. Names like `ServiceA`, `WebApi`, `DemoSystem`, `Worker` are neutral placeholders.

## Basic Building Blocks

Constructing the core primitives.

```fsharp
open Alma.Command
open Alma.ServiceIdentification

// Validated command name (Result — handle the error case)
let request =
    match Request.create "do_work" with
    | Ok r -> r
    | Error e -> failwith (RequestError.format e)

// Identity (Box) and target (BoxPattern)
let requestorBox = (Box.createFromStrings ("demo", "web-api", "common", "v1", "all", "common")).Value
let reactorPattern = (Box.createFromStrings ("demo", "worker", "common", "v1", "all", "common")).Value |> BoxPattern.ofBox

let requestor = Requestor requestorBox
let reactor = Reactor reactorPattern

// Validity window and auth
let ttl = TimeToLive.ofSeconds 5
let auth = AuthenticationBearer.empty

// Typed payload item: value + type tag
let nameItem : DataItem<string> = DataItem.createWithType ("example", "string")
```

## Realistic Custom Command

A reusable command module: private wrapper type, `create`, and DTO serialization.

```fsharp
[<RequireQualifiedAccess>]
module DoWorkCommand =
    open System
    open Alma.Command
    open Alma.Serializer
    open Feather.ErrorHandling

    let private request = Request "do_work"

    type CommandData = {
        Target: DataItem<string>
    }

    type Command = private Command of Command<MetaData, CommandData>

    [<RequireQualifiedAccess>]
    module Command =
        let internal command (Command command) = command

        let create requestor reactor authentication ttl replyTo target =
            let now = DateTime.Now
            let commandId = CommandId.create ()

            Command {
                Schema = 1
                Id = commandId
                CorrelationId = CorrelationId.fromCommandId commandId
                CausationId = CausationId.fromCommandId commandId
                Timestamp = now |> Serialize.dateTime

                TimeToLive = ttl
                AuthenticationBearer = authentication
                Request = request

                Reactor = reactor
                Requestor = requestor
                ReplyTo = replyTo

                MetaData = OnlyCreatedAt (CreatedAt now)
                Data = Data { Target = (target, "string") |> DataItem.createWithType }
            }

    // Domain -> DTO -> (later) JSON
    type DataDto = { target: DataItemDto<string> }
    type CommandDto = CommandDto<MetaDataDto.OnlyCreatedAt, DataDto>

    let serialize : Serialize<Command, MetaData, CommandData, CommandDto> =
        fun (Command command) ->
            command
            |> Command.toDto
                MetaDataDto.serialize
                (fun data -> Ok { target = data.Target |> DataItemDto.serialize id })
```

## Sending a Command (Integration)

Serialize to JSON, send, and parse the response.

```fsharp
open Alma.Command
open Alma.Serializer
open Feather.ErrorHandling
open Feather.ErrorHandling.Result.Operators

asyncResult {
    let command =
        DoWorkCommand.Command.create requestor reactor auth ttl ReplyTo.HttpCallerConnection "example"

    // Domain -> DTO (Result) -> JSON string
    let! commandDto =
        command
        |> DoWorkCommand.serialize
        |> AsyncResult.ofResult <@> DtoError.format

    let json = commandDto |> CommandDto.serialize Serialize.toJson

    // ...transport (HTTP/Kafka) returns a serialized response string...
    let! rawResponse = json |> WebApi.send |> AsyncResult.ofAsync

    // Parse the response (provide metadata + data parsers)
    let! response =
        rawResponse
        |> CommandResponse.parse (fun _ -> GenericMetaData.ofList []) (fun _ -> None)
        |> AsyncResult.ofResult <@> (sprintf "%A")

    return response
}
```

## Parsing an Incoming Command

Use `Command.parse` with a `RawData`-based payload parser.

```fsharp
open Alma.Command

let parseData (raw: RawData) : Result<Data<string>, CommandParseError> =
    match raw with
    | RawData.Item "target" (RawData.DataItem item) -> Ok (Data item.Value)
    | _ -> Error MissingData

let parsed : Result<Command<MetaData, string>, CommandParseError> =
    incomingJson
    |> Command.parse MetaData.parse parseData
```

## Command Handler

Validate and dispatch an incoming command.

```fsharp
open Alma.Command
open Feather.ErrorHandling

type HandleError = WorkFailed of string

let handler : CommandHandler<DoWorkCommand.Command, MetaData, DoWorkCommand.CommandData, string, HandleError> =
    CommandHandler.create
        (Request "do_work")
        DoWorkCommand.Command.command
        (fun command -> asyncResult {
            // ...do the work, produce optional response data...
            return Some (Data "done")
        })

let formatError (WorkFailed m) = m
let errorTitle (WorkFailed _) = "WorkFailed"

// Async responses are persisted via this callback; sync (HttpCallerConnection) bypass it
let persistAsync _commandId _replyTo _response = async { return () }

let result =
    CommandHandler.handle
        (fun _ -> Ok ())          // custom validation
        currentReactorBox         // this handler's Box
        formatError
        errorTitle
        persistAsync
        handler
        incomingCommand

match result with
| CommandStarted -> printfn "async work started"
| CommandNotStarted errors -> eprintfn "%A" errors
| CommandResponse response -> printfn "sync response: %A" response.Response
```

## Serialization Test (Expecto)

Normalize volatile fields before comparing JSON.

```fsharp
open Expecto
open Alma.Command
open Alma.Serializer

let private normalize (json: string) =
    let guid = @"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}"
    let time = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"
    let replace (pattern: string) (repl: string) (s: string) =
        System.Text.RegularExpressions.Regex.Replace(s, pattern, repl)
    json |> replace guid "uuid" |> replace time "time"

[<Tests>]
let tests =
    test "serializes a do_work command" {
        let json =
            DoWorkCommand.Command.create requestor reactor auth ttl ReplyTo.HttpCallerConnection "example"
            |> DoWorkCommand.serialize
            |> Result.map (CommandDto.serialize Serialize.toJson)

        match json with
        | Ok serialized ->
            Expect.stringContains (normalize serialized) "\"request\":\"do_work\"" "request name present"
        | Error e -> failtestf "%A" e
    }
```

## Full Workflow: Response to Kafka Event

Create a `CommandResponse` and derive a `CommandResponseCreated` event from it.

```fsharp
open System
open Alma.Command
open Alma.Command.Event
open Alma.ServiceIdentification

let response : CommandResponse<GenericMetaData, string> =
    CommandResponse.create
        correlationId
        causationId
        DateTime.Now
        (ReactorResponse reactorBox)
        (Requestor requestorBox)
        "http"
        (StatusCode System.Net.HttpStatusCode.Created)
        []                                  // no errors
        (Some (Data "done"))

let deriver : Box = reactorBox             // service emitting the event
let event = response |> CommandResponseCreated.deriveFromCommandResponse deriver commandId
```
