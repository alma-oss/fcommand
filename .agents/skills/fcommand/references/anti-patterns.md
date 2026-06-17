# Anti-Patterns

Each entry is **mistake → why → fix**.

## Command Shape

- **Treating `Command` as a `Synchronous | Asynchronous` DU.** → Older docs show two separate command cases, but the current API is a single generic record `Command<'MetaData,'CommandData>` that always carries a `ReplyTo` field. → Build the one record type and express sync-vs-async through `ReplyTo` (`ReplyTo.HttpCallerConnection` for synchronous), not through different command types.

- **Hardcoding `Schema` to an arbitrary number.** → Parsing rejects anything other than schema `1` (`UnsupportedSchema`). → Always set `Schema = 1`.

## Serialization

- **Serializing a domain `Command` (or `CommandResponse`) straight to JSON.** → Domain types contain wrapped DUs and are not the wire contract; output will be wrong or fail. → Always go domain → DTO (`Command.toDto` / `CommandResponse.toDto`) → JSON (`CommandDto.serialize` / `Serialize.toJson`).

- **Returning a plain value from a `toDto` serializer function.** → `Command.toDto` expects both the metadata and data serializers to return `Result<_,_>`; a bare value won't type-check. → Wrap success in `Ok` (e.g. `serializeData >> Ok`).

- **Formatting timestamps by hand.** → Inconsistent formats break round-tripping through the JSON type providers. → Use `Serialize.dateTime` (from `Alma.Serializer`) for timestamps and `created_at`.

## Construction & Validation

- **Building a `Request` directly or assuming `Request.create` succeeds.** → The `Request` constructor is private and `Request.create` returns `Error EmptyRequest` for null/empty input. → Call `Request.create` and handle the `Result`.

- **Ignoring the result of TTL/Reactor validation in a handler.** → A handler that skips validation will process expired or mis-routed commands. → Let `CommandHandler.handle` (or `handleWith` with explicit `Validations`) run the checks and branch on the returned `CommandHandleResult`.

- **Expecting a `CommandResponse` from every handler call.** → Only synchronous (`ReplyTo.HttpCallerConnection`) commands return `CommandResponse`; async ones return `CommandStarted` and deliver the response later via the persist callback. → Match all three `CommandHandleResult` cases (`CommandStarted`, `CommandNotStarted`, `CommandResponse`).

- **Assuming a parsed `CommandResponse` is always success.** → `CommandResponse.parse` returns `Error (ErrorResponse …)` when the status is `>= 400` or errors are present. → Handle the `Error` branch and inspect `CommandResponseError`.

## Routing & Spot

- **Reading the data `Spot` only from the `Reactor`.** → When the reactor pattern uses `*` (Any) for `Zone`/`Bucket`, the value comes from the `Requestor` instead. → Resolve `Zone` and `Bucket` independently, falling back to the requestor on wildcards.

## Raw JSON

- **Pattern-matching directly on `FSharp.Data.JsonValue` inside payload parsers.** → Brittle and ignores the provided helpers. → Use the `RawData` active patterns (`|Item|`, `|Itemi|`, `|DataItem|`, `|DataItemRaw|`, `|Json|`).

## Schemas & Build

- **Moving, renaming, or deleting files under `src/schema/`.** → `FSharp.Data.JsonProvider` reads them as compile-time samples; the build fails without them. → Keep the schema sample files in place and update the provider path if you intentionally relocate one.

- **Using `dotnet add package` to add dependencies.** → The project is managed by Paket, not the NuGet CLI. → Add packages via Paket (`paket.dependencies` / `paket.references`).

## Legacy / Dead Code

- **Reusing the commented-out `Transform.toInternal` / `toPublic` blocks in the event module.** → That code is disabled and not part of the public API. → Use the active `CommandResponseCreated.parse` / `deriveFromCommandResponse` functions only.
