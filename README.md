F-Command
=========

> Library which contains a Command types and basic modules.

---

## Install

Add following into `paket.dependencies`
```
git ssh://git@bitbucket.lmc.cz:7999/archi/nuget-server.git master Packages: /nuget/
# LMC Nuget dependencies:
nuget Lmc.Command
```

Add following into `paket.references`
```
Lmc.Command
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
    open Lmc.Command
    open Lmc.Command.CommonSerializer
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
open Lmc.Command

asyncResult {   // AsyncResult<CommandResponse, string>
    let myCommand =         // MyCommand.Command
        service
        |> MyCommand.Command.create
            currentApplication.Box
            (Reactor (api.Identification |> BoxPattern.ofServiceIdentification))
            api.Authentication
            (TimeToLive.ofSeconds 2)

    let! myCommandDto =     // Lmc.Command.CommandDto
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

    let! commandResponse =  // Lmc.Command.CommandResponse<NotParsed, NotParsed>
        response
        |> CommandResponse.parse <@> (sprintf "Error: %A")
        |> AsyncResult.ofResult

    return commandData
}
```

## Release
1. Increment version in `Command.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it
4. Run `$ fake build target release`
5. Go to `nuget-server` repo, run `faket build target copyAll` and push new versions

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)
- [FAKE](https://fake.build/fake-gettingstarted.html)

### Build
```bash
fake build
```

### Watch
```bash
fake build target watch
```
