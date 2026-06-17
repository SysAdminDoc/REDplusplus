# Contributing to RED++

## Reporting Bugs

Use the [bug report template](https://github.com/SysAdminDoc/REDplusplus/issues/new?template=bug_report.yml). Include your RED++ version (`RED+.exe -version`), Windows version, and the steps to reproduce. Attach `RED++.log` or `RED++.crash-*.txt` if available.

## Suggesting Features

Use the [feature request template](https://github.com/SysAdminDoc/REDplusplus/issues/new?template=feature_request.yml). Describe the problem or workflow first, then the proposed solution.

## Building

```
dotnet build "RED/RED+.csproj" -c Release
dotnet test "RED.Tests/RED.Tests.csproj" -c Release
```

Requires the .NET 9 SDK (or later with `latestFeature` roll-forward via `global.json`). No Visual Studio or MSBuild required.

## Pull Requests

1. Open an issue first to discuss the change.
2. Branch from `main`.
3. Follow the existing code style (no auto-formatting changes to unrelated lines).
4. Add or update tests for new behavior.
5. Verify: build succeeds, all tests pass, headless smoke test (`RED+.exe -silent -path <dir> -dryrun`) returns the expected exit code.
6. Update CHANGELOG.md for user-facing changes.

## License

By contributing, you agree that your contributions will be licensed under the [LGPL-3.0-or-later](LICENSE).
