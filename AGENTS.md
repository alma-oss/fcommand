# Alma.Command (fcommand)

This repo ships Agent Skill for the `Alma.Command` library. Compatible agents discover it automatically; see `.agents/skills/fcommand/SKILL.md`.

## Project Purpose

Open-source F# library (`Alma.Command` NuGet package) providing generic Command types, serialization/deserialization, command handler infrastructure, and command response handling for the Alma platform's CQRS/event-driven architecture. Used by downstream microservices to define, send, parse, and handle domain commands over Kafka.

## Tech Stack

- **Language:** F# (.NET 10.0)
- **Build system:** FAKE (F# Make) via `build.sh` wrapper
- **Package management:** Paket (`paket.dependencies` / `paket.references`)
- **Test framework:** Expecto
- **Linter:** FSharpLint (`fsharplint.json`)
- **CI/CD:** GitHub Actions
- **Key dependencies:**
  - `FSharp.Core` ~> 10.0
  - `FSharp.Data` ~> 6.0 (JSON type providers for schema-based parsing)
  - `Feather.ErrorHandling` ~> 2.0 (Result/AsyncResult computation expressions)
  - `Alma.Kafka` ~> 30.0 (Kafka event types: `Event`, `EventId`, `EventName`, `MetaData`, etc.)
  - `Alma.Serializer` ~> 9.0 (JSON serialization, `Serialize.dateTime`, `Serialize.toJson`)
  - `Alma.ServiceIdentification` ~> 11.0 (Domain/Context/Purpose/Version/Zone/Bucket — "Box" model)

## Commands

```bash
# Restore tools + packages and build
./build.sh build

# Run tests
./build.sh -t tests

# Lint
./build.sh -t lint

# Pack NuGet package
./build.sh -t release

# Publish to NuGet.org (requires NUGET_API_KEY)
./build.sh -t publish
```

`build.sh` runs: `dotnet tool restore` → `dotnet tool run paket restore` → FAKE build pipeline.

## Project Structure

```
Command.fsproj          # Library project (OutputType: Library)
AssemblyInfo.fs         # Auto-generated assembly metadata

src/
  Utils.fs              # Internal EventType assertion helper
  Types.fs              # Core domain types: CommandId, CorrelationId, CausationId,
                        #   Reactor, Requestor, ReplyTo, TimeToLive,
                        #   AuthenticationBearer, Request, DataItem, Data,
                        #   RawData (active patterns for JSON parsing),
                        #   GenericMetaData, and all DTO types
  MetaData.fs           # MetaData (OnlyCreatedAt), parsing, DTO serialization
  Command.fs            # Generic Command<'MetaData, 'CommandData> record,
                        #   CommandDto, serialization (Command.toDto),
                        #   parsing (Command.parse via JSON type provider),
                        #   bind operations (bindMetaData, bindData)
  Response.fs           # CommandResponse<'MetaData, 'ResponseData>,
                        #   ResponseError, StatusCode, parsing, serialization
  CommandHandler.fs     # CommandHandler infrastructure: validation (TTL, Reactor),
                        #   Spot resolution, synchronous/asynchronous dispatch,
                        #   response creation, error formatting

  Command/
    StartProcessCommand.fs  # Prepared "start_process" command implementation

  Event/
    Event.fs                    # Event DtoError types, Transform module
    CommandResponseCreated.fs   # "command_response_created" event definition

  schema/               # JSON schema files used by FSharp.Data type providers

tests/
  Tests.fs                      # Expecto test entry point
  StartProcessCommandTest.fs    # Tests for StartProcessCommand
  CommandResponseCreatedTest.fs # Tests for CommandResponseCreated event

build/
  Build.fs              # FAKE build project definition
  Targets.fs            # FAKE targets (Clean, Build, Lint, Tests, Release, Publish)
  Utils.fs              # Build utility functions
  SafeBuildHelpers.fs   # SAFE Stack build helpers (not used in this library)
```

## Architecture & Domain Concepts

### Service Identification ("Box" model)
Every service is identified by six dimensions: **Domain / Context / Purpose / Version / Zone / Bucket**. A `Box` is a concrete instance; a `BoxPattern` supports wildcards (`*`) for routing.

### Command Flow
1. Create a typed command (e.g., `StartProcess.Command.create`)
2. Serialize to `CommandDto` via `Command.toDto` with metadata + data serializers
3. Serialize DTO to JSON string via `CommandDto.serialize Serialize.toJson`
4. Send over Kafka (or HTTP)
5. Receiver parses with `Command.parse` (uses JSON type provider against `src/schema/command.json`)
6. `CommandHandler` validates (TTL, Reactor matching) and dispatches
7. Response is created as `CommandResponse` and returned or published

### Reactor & Spot Resolution
- **Reactor** identifies the target command handler (as `BoxPattern`)
- **Spot** (Zone + Bucket) is resolved from `Reactor` if specified, or falls back to `Requestor`'s Spot when Reactor uses `*` (Any)

### ReplyTo
- `ReplyTo.HttpCallerConnection` → synchronous HTTP response
- Other types → asynchronous via Kafka topic

## Conventions

- **Single-case DU wrappers** for domain primitives (`CommandId of Guid`, `Request of string`, etc.)
- **`[<RequireQualifiedAccess>]`** on all modules — always use qualified access (`CommandId.value`, not `value`)
- **Result-based error handling** everywhere — use `Feather.ErrorHandling` computation expressions (`result { }`, `asyncResult { }`)
- **`<@>` operator** for `Result.mapError`
- **JSON type providers** (`FSharp.Data.JsonProvider`) for parsing — schemas live in `src/schema/`
- **DTO pattern**: Domain type → DTO type → serialized JSON. Never serialize domain types directly.
- **Naming**: module names match the type they operate on (e.g., `module CommandId` operates on `CommandId` type)
- **No mutable state** — all types are immutable records or DUs
- **`internal`** visibility for implementation details not part of the public API

## CI/CD Workflows

| Workflow | Trigger | What it does |
|----------|---------|-------------|
| `tests.yaml` | PR, daily cron (3 AM) | Runs `./build.sh -t tests` on ubuntu-latest with .NET 10.x |
| `pr-check.yaml` | PR | Blocks fixup commits, runs ShellCheck on shell scripts |
| `publish.yaml` | Tag push (`X.Y.Z`) | Runs `./build.sh -t publish` to publish NuGet package |

## Release Process

1. Increment `<Version>` in `Command.fsproj`
2. Update `CHANGELOG.md` (move items from Unreleased to new version section)
3. Commit and push
4. Create a git tag matching the version (e.g., `14.0.0`) — this triggers the publish workflow

## Pitfalls

- **JSON schema files are required at compile time** — `FSharp.Data.JsonProvider` uses `src/schema/*.json` as compile-time samples. Do not move or rename them without updating the corresponding type provider paths in source files.
- **No docker-compose** — this is a library, not a service. No local environment to spin up.
- **Paket, not NuGet CLI** — always use `dotnet paket install` to add packages, not `dotnet add package`.
- **FAKE build system** — the `build/` directory contains the FAKE build project. The entry point is `build.sh`, not `dotnet build` directly (though `dotnet build` works for compilation).
- **`build.sh` requires bash** — uses `set -eu -o pipefail`.
- **`Alma.*` packages** are internal/OSS Alma ecosystem packages — check their repos for API docs.
