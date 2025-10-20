# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 12.1.0 - 2025-10-20
- Update dependencies

## 12.0.0 - 2025-03-18
- [**BC**] Use net9.0

## 11.0.0 - 2024-01-11
- [**BC**] Use net8.0
- Fix package metadata

## 10.0.0 - 2023-09-11
- [**BC**] Use `Alma` namespace

## 9.0.0 - 2023-08-11
- [**BC**] Use net7.0

## 8.2.0 - 2023-06-30
- Update dependencies

## 8.1.0 - 2022-08-26
- Update dependencies

## 8.0.0 - 2022-05-19
- Update dependencies
    - [**BC**] Use `OpenTelemetry` tracing

## 7.3.0 - 2022-03-04
- Update dependencies

## 7.2.0 - 2022-02-22
- Update dependencies

## 7.1.0 - 2022-01-25
- Update kafka library
- Add function `CommandResponseCreated.Event.key`

## 7.0.0 - 2022-01-07
- [**BC**] Use net6.0

## 6.2.0 - 2021-10-29
- Update dependencies
- Add `BoxError` to `InvalidRequestor` and `InvalidReactor` errors

## 6.1.0 - 2021-10-11
- Update dependencies

## 6.0.0 - 2021-09-29
- [**BC**] Change `Command` type
    - Remove `Synchronous/Asynchronous` variants
- [**BC**] Rename `CommandHandleResult` cases
    - `AsynchronousCommandStarted` -> `CommandStarted`
    - `AsynchronousCommandNotStarted` -> `CommandNotStarted`
    - `SynchronousCommandResponse` -> `CommandResponse`
- [**BC**] Remove support for old command response format

## 5.8.0 - 2021-09-29
- Add `DataItemDto.serializeResult` function

## 5.7.0 - 2021-09-27
- Update dependencies

## 5.6.0 - 2021-06-16
- Validate ttl from both both sides of the time

## 5.5.0 - 2021-06-01
- Allow empty data in command response

## 5.4.0 - 2021-06-01
- Add literals for `ReplyTo`
    - `ReplyTo.TypeHttp`
    - `ReplyTo.IdentificationHttp`
- Add predefined value `ReplyTo.Http`

## 5.3.0 - 2021-03-10
- Change ttl validation to allow `0` (_or negative_) ttl as **infinite**

## 5.2.0 - 2021-03-09
- Change `Command.parse` to parse Synchronous Command even if it has a `replyTo` field, with direct response value
- Allow response `id` either in the attributes or correctly in the resource.id field or in the attributes.response (*as it should be everywhere*)
- Add `CommandResponseDto.toJson` function

## 5.1.0 - 2021-02-16
- Update dependencies

## 5.0.0 - 2020-12-18
- [**BC**] Change `CommandHandler` handle functions to create a CommandResponse and handle synchronous/asynchronous command directly

## 4.1.0 - 2020-12-11
- Add `Command.id` function
- Add `CommandId.tryParse` function
- Add `Command.toCommon` and a `CommonCommandData` type
- Add `CommandHandler` type and module

## 4.0.0 - 2020-12-01
- [**BC**] Use .net 5.0

## 3.0.0 - 2020-12-01
- Add `Command.parse` function
- Add `RawData` module with active patterns for easier parsing a data out of a command
- Update dependencies
- Add `CommandResponseCreated` Event
- [**BC**] Parse data in `CommandResponse.parse` function

## 2.0.0 - 2020-05-13
- [**BC**] Change all DTO structures to PascalCase, and let the serialization dealt with a format
- Add `StartProcessCommand` module
- Add tests

## 1.1.0 - 2020-04-15
- Make `CommonSerializer` public

## 1.0.0 - 2020-04-15
- Initial implementation
