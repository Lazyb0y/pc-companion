# Build

```
dotnet build PCCompanion.slnx -c Debug -p:Platform=x64
```

Platforms: `x64` and `ARM64`.

- .NET 10 (LTS). The SDK is pinned in `global.json` and rolls forward to the latest installed feature band, which Visual Studio updates replace. It never rolls forward to previews.
- Package versions are set centrally in `Directory.Packages.props`. `packages.lock.json` files are committed.
- `Directory.Build.props` enables nullable reference types, `latest-recommended` analyzers and warnings as errors.
- Vulnerability advisories: low and moderate are warnings; high and critical fail the build.
- Release builds aren't trimmed. The default Windows SDK projection has trim warnings, and Release-only trimming breaks locked restore.
- `.editorconfig` covers formatting only; code style isn't enforced in the build.
