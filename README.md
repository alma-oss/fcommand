F-Command
=========

> Library which contains a Command types and basic modules.

---

## Install

Add following into `paket.dependencies`
```
source https://nuget.pkg.github.com/almacareer/index.json username: "%PRIVATE_FEED_USER%" password: "%PRIVATE_FEED_PASS%"
# LMC Nuget dependencies:
nuget Alma.Command
```

NOTE: For local development, you have to create ENV variables with your github personal access token.
```sh
export PRIVATE_FEED_USER='{GITHUB USERNANME}'
export PRIVATE_FEED_PASS='{TOKEN}'	# with permissions: read:packages
```

Add following into `paket.references`
```
Alma.Command
```

## Command types
There are 2 generic command types:

```fs
type Command<'MetaData, 'CommandData> =
    | Synchronous of SynchronousCommand<'MetaData, 'CommandData>
    | Asynchronous of AsynchronousCommand<'MetaData, 'CommandData>
```

### Synchronous
```fs
type SynchronousCommand<'MetaData, 'CommandData> = {
    Schema: int
    Id: CommandId
    CorrelationId: CorrelationId
    CausationId: CausationId
    Timestamp: string

    TimeToLive: TimeToLive
    AuthenticationBearer: AuthenticationBearer
    Request: Request

    Reactor: Reactor
    Requestor: Requestor

    MetaData: 'MetaData
    Data: Data<'CommandData>
}
```

### Asynchronous
```fs
type AsynchronousCommand<'MetaData, 'CommandData> = {
    Schema: int
    Id: CommandId
    CorrelationId: CorrelationId
    CausationId: CausationId
    Timestamp: string

    TimeToLive: TimeToLive
    AuthenticationBearer: AuthenticationBearer
    Request: Request

    Reactor: Reactor
    Requestor: Requestor
    ReplyTo: ReplyTo

    MetaData: 'MetaData
    Data: Data<'CommandData>
}
```

## Use
Create your own command module:

```fs
[<RequireQualifiedAccess>]
module MyCommand =
    open System
    open Alma.Command
    open Alma.Command.CommonSerializer
    open ServiceIdentification

    let private request = "my_command_name" |> Request.create |> Result.orFail

    type CommandData = {
        Service: DataItem<Service>
    }

    type Command = private Command of SynchronousCommand<MetaData, CommandData>

    [<RequireQualifiedAccess>]
    module Command =
        let internal command (Command command) = command

        let create currentApplication reactor authentication ttl commit service =
            let now = DateTime.Now
            let commandId = Guid.NewGuid() |> CommandId

            let commandData = {
                Service = (service, "string") |> DataItem.createWithType
            }

            Command {
                Schema = 1
                Id = commandId
                CorrelationId = CorrelationId.fromCommandId commandId
                CausationId = CausationId.fromCommandId commandId
                Timestamp = now |> formatDateTime

                TimeToLive = ttl
                AuthenticationBearer = authentication
                Request = request

                Reactor = reactor
                Requestor = Requestor currentApplication

                MetaData = OnlyCreatedAt (CreatedAt now)
                Data = Data commandData
            }

    //
    // Serialize DTO
    //

    type DataDto = {
        service: DataItemDto<string>
    }

    type CommandDto = CommandDto<MetaDataDto.OnlyCreatedAt, DataDto>

    [<RequireQualifiedAccess>]
    module private Dto =
        let private serializeData data =
            {
                service = data.Service |> DataItemDto.serialize (Service.concat "-")
            }

        let fromCommand command =
            Command.Synchronous command
            |> Command.toDto
                MetaDataDto.serialize
                (serializeData >> Ok)

    // Public DTO functions

    let serialize: Serialize<Command, MetaData, CommandData, CommandDto> =
        fun (Command command) ->
            command |> Dto.fromCommand
```

Then use your command:

```fs
open Alma.Command

asyncResult {   // AsyncResult<CommandResponse, string>
    let myCommand =         // MyCommand.Command
        service
        |> MyCommand.Command.create
            currentApplication.Box
            (Reactor (api.Identification |> BoxPattern.ofServiceIdentification))
            api.Authentication
            (TimeToLive.ofSeconds 2)

    let! myCommandDto =     // Alma.Command.CommandDto
        myCommand
        |> MyCommand.serialize
        |> AsyncResult.ofResult <@> DtoError.format

    let serializedCommand = // string
        myCommandDto
        |> CommandDto.serialize Serialize.toJson

    let! response =         // string
        serializedCommand
        |> Api.sendCommand api
        |> AsyncResult.ofAsync

    let! commandResponse =  // Alma.Command.CommandResponse<NotParsed, NotParsed>
        response
        |> CommandResponse.parse <@> (sprintf "Error: %A")
        |> AsyncResult.ofResult

    return commandData
}
```

## Command handler
> Command handler is a common way of handling commands.

### Validation
Before executing a command, there are a few validations for a given command.

1. TTL
    - check, that Command.timestamp + Command.ttl is still valid (`validFrom <= now && now <= validTo`)
    - it should end with `408 Timeout`, if it is not valid
    ```
            [<- valid ->]
            ┌───────────┐
            │           │
    --------------------------------------> t
            │           │
            timestamp   ttl
    ```

2. Reactor
    - Reactor is matched based on Command.reactor pattern
    - if a reactor (_current command handler_) is not matching a Command.reactor _pattern_, it ends with an Error

### Spot for data
Spot is determine based on a Command.reactor and Command.requestor as follows

1. Specified by reactor
    - if a Command.reactor (_box pattern_) contains a predefined Spot, it is used as is

    Command
    ```json
    {
        ...
        "reactor": {
            ...
            "zone": "my",
            "bucket": "data"
        },
        "requestor": {
            ...
            "zone": "some",
            "bucket": "bucket"
        },
        ...
    }
    ```
    Spot
    ```json
    {
        "zone": "my",
        "bucket": "data"
    }
    ```

2. Unspecified by reactor
    - if a Command.reactor (_box pattern_) contains a `*` (_Any_) in the Spot, a Command.requestor.spot is used

    Command
    ```json
    {
        ...
        "reactor": {
            ...
            "zone": "*",
            "bucket": "*"
        },
        "requestor": {
            ...
            "zone": "some",
            "bucket": "bucket"
        },
        ...
    }
    ```
    Spot
    ```json
    {
        "zone": "some",
        "bucket": "bucket"
    }
    ```

**NOTE**: It applies for `Spot`, `Zone` and `Bucket` in the same way

Command
```json
{
    ...
    "reactor": {
        ...
        "zone": "all",
        "bucket": "*"
    },
    "requestor": {
        ...
        "zone": "some",
        "bucket": "bucket"
    },
    ...
}
```
Spot
```json
{
    "zone": "all",
    "bucket": "bucket"
}
```

## Release
1. Increment version in `Command.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)

### Build
```bash
./build.sh build
```

### Tests
```bash
./build.sh -t tests
```
