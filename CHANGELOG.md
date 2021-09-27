# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
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
