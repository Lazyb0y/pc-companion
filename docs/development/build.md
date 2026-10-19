# Build

```
dotnet build PCCompanion.slnx -c Debug -p:Platform=x64
```

Platforms: `x64` and `ARM64`.

Tests, and the checks to run before a pull request, are in [Testing](testing.md).

## Configuration

- .NET 10 (LTS). The SDK is pinned in `global.json` and rolls forward to the latest installed feature band, which Visual Studio updates replace. It never rolls forward to previews.
- Package versions are set centrally in `Directory.Packages.props`. `packages.lock.json` files are committed.
- `Directory.Build.props` enables nullable reference types, `latest-recommended` analyzers and warnings as errors.
- Vulnerability advisories: low and moderate are warnings; high and critical fail the build.
- Release builds aren't trimmed. The default Windows SDK projection has trim warnings, and Release-only trimming breaks locked restore.
- Project references don't receive the `PublishDir` of the project that references them, so the libraries the settings app and the host share build only once when the installer publishes both.
- `.editorconfig` covers formatting only; code style isn't enforced in the build.
- CodeQL scans the C# code and the GitHub Actions workflows with the extended security queries on pushes to `main`, pull requests and once a week. Results appear under Security → Code scanning.
