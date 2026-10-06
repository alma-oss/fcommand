# Preferred Patterns

## Core Principles

- **DTO pattern is mandatory.** Never serialize a domain type directly. Always convert `Command` → `CommandDto` (via `Command.toDto`) and only then to JSON (via `CommandDto.serialize`). The same applies to responses (`CommandResponse` → `CommandResponseDto` → JSON).
- **Domain primitives are single-case DUs.** `CommandId`, `Request`, `TimeToLive`, etc. wrap raw values. Construct them through their module (`CommandId.create`, `Request.create`, `TimeToLive.ofSeconds`) and read them with `.value`.
- **All modules use `[<RequireQualifiedAccess>]`.** Always qualify: `Request.create`, `CommandId.value`, `Data.data` — never the bare functions.
- **Commands are immutable records.** Build them once at creation; transform with `Command.bindMetaData` / `Command.bindData` rather than mutating.
- **Keep your `Command` constructor private.** Wrap the generic `Command<_,_>` in a private single-case DU inside your module and expose only `create` + `serialize` (see `examples.md` → Realistic Custom Command).

## Recommended API Usage

- **Creating a command:** assemble a `Command<'MetaData,'CommandData>` record with `Schema = 1`, IDs from `CommandId.create`, correlation/causation via `CorrelationId.fromCommandId` / `CausationId.fromCommandId`, `Reactor` from a `BoxPattern`, `Requestor` from a `Box`, and a `ReplyTo`. See `examples.md` → Realistic Custom Command.
- **Payloads:** wrap each field in a `DataItem` using `DataItem.createWithType (value, "type")` (explicit type tag) or `DataItem.create value` (inferred). Wrap the whole payload in `Data`.
- **Serializing:** `Command.toDto serializeMetaData serializeData` returns `Result<CommandDto,_>`; both serializer functions must return `Result`. Then `CommandDto.serialize Serialize.toJson`.
- **Parsing:** `Command.parse parseMetaData parseData json` returns `Result<Command<_,_>, CommandParseError>`. Provide a metadata parser and a data parser that each consume a `RawData`.
- **Hand-parsing raw payloads:** use the `RawData` active patterns (`|Item|`, `|Itemi|`, `|DataItem|`, `|DataItemRaw|`, `|Json|`) instead of reaching into `JsonValue` directly. See `examples.md` → Parsing an Incoming Command.
- **Metadata:** for the simple case use the built-in `MetaData.OnlyCreatedAt` with `MetaDataDto.serialize` / `MetaData.parse`.

## Error Handling

- Everything is `Result`/`AsyncResult`-based. Compose inside `result { }` / `asyncResult { }` from `Feather.ErrorHandling`.
- Map errors with the `<@>` operator (`Result.mapError`) at each boundary, e.g. turn a `DtoError` into a string with `<@> DtoError.format`.
- Convert error cases to text with the provided formatters: `DtoError.format`, `CommandHandleError.format`, `ResponseError.format`, `RequestError.format`, `MetaDataParseError.format`.
- `Request.create` rejects null/empty with `EmptyRequest` — always handle the `Result`, never assume success.

## Composition

- **Change metadata type:** `Command.bindMetaData (f: 'MetaData -> Result<'NewMetaData,'Error>)` rebuilds the command preserving the envelope.
- **Change data type:** `Command.bindData (f: Data<'Data> -> Result<Data<'NewData>,'Error>)`.
- **Drop envelope detail:** `Command.toCommon` projects to `CommonCommandData` for validation/routing logic.
- **Match on request name:** the `Command.(|OfRequest|_|)` active pattern routes by `Request`.

## Command Handling

- Build a handler with `CommandHandler.create request getCommand handle`, where `handle` returns `AsyncResult<Data<'ResponseData> option, CommandHandleError<'Error>>`.
- Dispatch with `CommandHandler.handle` (uses `defaultValidations`) or `CommandHandler.handleWith` to supply custom `Validations`.
- **`Validations`** independently toggle `TimeToLive` and `Reactor` checks (`Validation.Validate` | `Validation.Ignore`); `defaultValidations` validates both.
- **TTL validation** rejects a command whose `timestamp … timestamp + ttl` window no longer contains "now" with `408 Timeout`.
- **Reactor validation** rejects the command unless the handler's `Box` matches the command's `Reactor` `BoxPattern`.
- **Reply behavior:** when `ReplyTo` is `ReplyTo.HttpCallerConnection` the handler runs synchronously and returns a `CommandResponse`; otherwise it starts async work and returns `CommandStarted`, persisting the response through the supplied callback.
- **Spot resolution:** the data `Spot` is taken from the `Reactor`; each of `Zone` and `Bucket` independently falls back to the `Requestor`'s value when the reactor pattern holds `*` (Any).

## Integration with Other Libraries

- `Reactor` wraps a `BoxPattern` and `Requestor`/`ReactorResponse` wrap a `Box` from `Alma.ServiceIdentification`; build them with `BoxPattern.createFromStrings` / `Create.Box` / `Box.createFromStrings`.
- Use `Serialize.toJson` for the final JSON string and `Serialize.dateTime` for timestamps from `Alma.Serializer`.
- The prepared `CommandResponseCreated` event (namespace `Alma.Command.Event`) bridges a `CommandResponse` onto a Kafka event via `CommandResponseCreated.deriveFromCommandResponse`. See `examples.md` → Full Workflow.

## Naming Conventions

- A module's name matches the type it operates on (`module CommandId` operates on `CommandId`).
- DTO types and modules carry the `Dto` suffix (`CommandDto`, `DataItemDto`, `ResponseErrorDto`).
- Mark implementation-only helpers `internal` / `private`; expose a minimal public surface (`create`, `serialize`, `parse`).

## Build & Testing Recommendations

- Build with `./build.sh build` and run tests with `./build.sh -t tests` (Expecto). The `build.sh` wrapper restores Paket packages first.
- JSON schema samples live under `src/schema/` and are consumed by `FSharp.Data.JsonProvider` at compile time — tests rely on them being present.
- Test serialization by normalizing volatile fields (UUIDs, timestamps) before comparing against an expected JSON string. See `examples.md` → Serialization Test.
