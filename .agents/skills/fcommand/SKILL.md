---
name: fcommand
description: Use whenever generating or reviewing F# code that defines, serializes, parses, sends, or handles commands with the Alma.Command library — e.g. calling Command.toDto, Command.parse, CommandDto.serialize, CommandResponse.create/parse/toDto, or composing a CommandHandler (CommandHandler.create/handle/handleWith). Trigger also on mentions of Reactor, Requestor, ReplyTo, TimeToLive, DataItem, RawData, GenericMetaData, CommandResponseCreated events, TTL/Reactor command validation, Spot/Zone/Bucket resolution, or the StartProcess prepared command. Applies to CQRS command flow over Kafka or HTTP in the Alma ecosystem.
---

# F-Command

Library: [alma-oss/fcommand](https://github.com/alma-oss/fcommand)  NuGet: `Alma.Command`

## Purpose

`Alma.Command` provides generic, strongly-typed Command and CommandResponse types plus the infrastructure to serialize them to DTOs/JSON, parse them back via JSON type providers, and dispatch them through a validating CommandHandler. It implements a CQRS/event-driven command flow where commands are routed to a target service (Reactor) and answered synchronously (HTTP) or asynchronously (e.g. Kafka).

## When to Use

- Defining a new typed command module (request name + typed `Data`) for a service.
- Serializing a command to JSON before sending, or parsing an incoming command/response.
- Building a command handler that validates and dispatches an incoming command.
- Producing a `CommandResponse`, or deriving a `CommandResponseCreated` event from one.

## When NOT to Use

- Plain event publishing/consuming without command semantics (use the Kafka event library directly).
- Service identity/routing primitives themselves (those come from `Alma.ServiceIdentification`).
- Generic JSON serialization unrelated to commands (use `Alma.Serializer`).

## Main Concepts

- **`Command<'MetaData, 'CommandData>`** — generic command record carrying envelope fields plus typed `MetaData` and `Data`.
- **`CommandDto<'MetaDataDto, 'DataDto>`** — wire/JSON shape of a command; produced via `Command.toDto`.
- **`Data<'CommandData>` / `DataItem<'Value>`** — typed payload wrappers; `DataItem` pairs a value with a `Type` tag.
- **`Reactor`** — target handler address as a `BoxPattern` (supports `*` wildcards).
- **`Requestor`** — sender identity as a concrete `Box`.
- **`ReplyTo`** — where the response goes; `ReplyTo.HttpCallerConnection` means synchronous HTTP reply.
- **`TimeToLive`** — command validity window (`TimeToLive.ofSeconds` / `ofMiliSeconds`).
- **`Request`** — validated command-name string (`Request.create` returns a `Result`).
- **`CommandResponse<'MetaData, 'ResponseData>`** — typed response with `StatusCode` and `ResponseError list`.
- **`CommandHandler<...>`** — bundles a `Request`, a command accessor, and an async handler; created with `CommandHandler.create`.
- **`CommandHandleResult`** — outcome DU: `CommandStarted` | `CommandNotStarted` | `CommandResponse`.
- **`RawData`** — untyped JSON wrapper with active patterns (`|Item|`, `|DataItem|`, …) for hand-parsing payloads.
- **`GenericMetaData`** — `Map<string,string>` metadata used by responses.
- **`Spot` (Zone + Bucket)** — data placement resolved from `Reactor`, falling back to `Requestor` on wildcards.

## Related Libraries

- `Alma.ServiceIdentification` — `Box` / `BoxPattern` (Domain/Context/Purpose/Version/Zone/Bucket) used by Reactor/Requestor.
- `Alma.Serializer` — JSON serialization (`Serialize.toJson`, `Serialize.dateTime`).
- `Alma.Kafka` — event envelope types used by the prepared `CommandResponseCreated` event.
- `Feather.ErrorHandling` — `result`/`asyncResult` computation expressions and the `<@>` map-error operator.

## Keywords for Search

Alma.Command, fcommand, F# command, CQRS, command handler, Command.toDto, Command.parse, CommandDto.serialize, CommandResponse, CommandResponse.parse, CommandHandler.handle, Reactor, Requestor, ReplyTo, HttpCallerConnection, TimeToLive, TTL validation, DataItem, Data, RawData, GenericMetaData, Spot, Zone, Bucket, BoxPattern, StartProcess, CommandResponseCreated, JSON type provider, DTO serialization

## Reference Files

- For composition principles, recommended API usage, error handling, and testing, read `references/preferred-patterns.md`.
- For known pitfalls, outdated assumptions, and incorrect usage, read `references/anti-patterns.md`.
- For worked, runnable code examples (ordered simple to full workflow), read `references/examples.md`.
