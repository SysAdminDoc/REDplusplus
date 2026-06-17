## What

Brief description of the change.

## Why

What problem does this solve?

## Checklist

- [ ] `dotnet build "RED/RED+.csproj" -c Release` succeeds with no warnings
- [ ] `dotnet test "RED.Tests/RED.Tests.csproj" -c Release` passes
- [ ] Headless smoke test: `RED+.exe -silent -path <temp tree> -dryrun` returns expected exit code
- [ ] CHANGELOG.md updated (if user-facing)
- [ ] README.md updated (if it affects usage or setup)
