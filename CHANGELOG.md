# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
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
