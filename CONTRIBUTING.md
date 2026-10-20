# Contributing

Thanks for your interest in PC Companion. Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md).

## Before you start

- Report bugs and suggest features with the [issue templates](https://github.com/Lazyb0y/pc-companion/issues/new/choose). Ask questions and discuss early ideas in [Discussions](https://github.com/Lazyb0y/pc-companion/discussions).
- Report security problems privately, as described in [SECURITY.md](SECURITY.md), never in public issues.
- For anything bigger than a small fix, comment on the issue and wait for the maintainer to agree on the approach before you start. New features go through the issue and the project board first.

## Making a change

1. Fork the repository and create a branch from `main`, named `<type>/<topic>`, for example `fix/tray-menu`.
2. Build as described in [Build](docs/development/build.md): Windows 10 version 2004 or later, and the .NET SDK version in `global.json`. Test as described in [Testing](docs/development/testing.md). Before opening a pull request, run `scripts\verify.ps1`, which runs the same checks as CI.
3. Keep each commit to one logical change that builds with no warnings. Write commit messages in the [Conventional Commits](https://www.conventionalcommits.org/) style and reference the issue, for example `fix(host): keep the tray icon after Explorer restarts (#42)`.
4. Add or update tests. Tests use their own temporary data and never touch windows or files they didn't create.
5. Update the documentation in `docs/` when behavior changes.
6. Open a pull request using the template. The required checks must pass and the branch must be up to date with `main`; the maintainer merges with "Rebase and merge".

## Guidelines

- Fix warnings rather than suppressing them; analyzers run with warnings as errors. Match the style of the surrounding code and `.editorconfig`.
- Discuss new projects and new packages first. Don't add a library when a small helper will do.
- No user-facing text in code or XAML: add it to `Strings.resx`, as described in [Localization](docs/localization/overview.md).
- Support keyboard use, screen readers, high DPI, light and dark themes, and right-to-left languages.
- Support Windows 10 version 2004 and later; see [Windows support](docs/compatibility/windows-support.md).
- No telemetry. The app connects to the network only when a feature or updates need it: discuss a new connection in its issue first, and describe it in [PRIVACY.md](PRIVACY.md). Treat external input as untrusted, and start processes with `ProcessStartInfo.ArgumentList`, never through a shell.
- Never copy code from GPL-licensed projects, such as KDE, AltSnap, CopyQ or Rainmeter, including close ports or translations. Studying how they behave is fine.

## Translations

- To improve a translation, edit `src/PCCompanion.Localization/Strings.<language>.resx` and remove the "Needs native review." comment from the strings you reviewed.
- To add a language, add `Strings.<language>.resx` with every string. No code changes are needed.

## AI tools

AI coding agents working in this repository follow [AGENTS.md](AGENTS.md). Whatever tools you use, you are responsible for the changes you submit.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
