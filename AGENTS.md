# Agent instructions

Instructions for AI coding agents working in this repository. Read this file before doing anything.

## Approval and change control

These rules are mandatory. The full policy is in [docs/project/change-control.md](docs/project/change-control.md).

- Do not create, modify, rename, move or delete any file unless the maintainer explicitly authorizes that specific change. Research, proposals and plan approval are not authorization. Permission for one task does not cover another. When unsure, ask.
- If the maintainer says "do not change anything", the repository is read-only until they authorize changes again.
- Work in small tasks: propose the next task, wait for authorization, implement only that task, validate it, report and stop.
- Never stage, commit, push, publish releases or rewrite Git history without explicit permission. Never overwrite or discard existing changes without approval.
- Make no changes on GitHub (issues, labels, milestones, project items, settings, releases) unless the maintainer asks for that action. Read-only `gh` commands are fine.
- Discuss significant decisions first: present alternatives and trade-offs, recommend one and let the maintainer decide.
- Never silently remove, contradict or weaken existing requirements, including when editing this file.

After each task, report: summary, files changed, decisions and rationale, validation results, manual verification steps, remaining concerns and a suggested conventional commit message.

## Project

PC Companion is a free, open-source (MIT) Windows app, written in C# on .NET, that brings useful features from macOS, Linux, KDE Plasma, GNOME and other desktop environments to Windows. It is one lightweight, modular app in which users enable and configure individual features. It complements Windows and existing tools rather than duplicating them.

Guiding principles: incremental delivery, human-controlled changes, modular architecture, high-quality UI, automated testing, security, privacy and long-term maintainability.

## Repository

- `PCCompanion.slnx` is the solution. Projects go in `src/`, test projects in `tests/`, documentation in `docs/`.
- `website/` is the project website, published with GitHub Pages at https://lazyb0y.github.io/pc-companion/; follow [Website](docs/development/website.md). It describes the latest release: update it, with screenshots from `scripts/website-screenshots.ps1`, when a release adds or changes features. Plain HTML and CSS, with nothing loaded from other sites and no analytics.
- The backlog, roadmap, milestones and task status live on GitHub (issues and the "PC Companion" project board), not in the repository.
- Files start minimal and grow with the repository. Committed files contain no future-planning content.
- Record significant decisions in the matching topic doc under `docs/` (see [docs/README.md](docs/README.md)).

## Build

- Build with `dotnet build PCCompanion.slnx -c Debug -p:Platform=x64`. Platforms are `x64` and `ARM64`.
- `scripts/verify.ps1` builds every configuration, runs the tests and checks the lock files, as CI does; use it to validate a task.
- The .NET SDK is pinned in `global.json`. Package versions are managed centrally in `Directory.Packages.props`, and `packages.lock.json` files are committed.
- `Directory.Build.props` enables nullable reference types, `latest-recommended` analyzers and warnings as errors. Fix warnings; don't suppress them without approval.
- New projects and new packages need maintainer approval. Don't add a library when only one or two simple methods are needed; write them.

## Architecture

- A small background host (tray icon, hooks, enabled features) plus a WinUI 3 settings app that is launched on demand and exits when closed. The settings app is the only process that writes settings; the host watches the settings files. An elevated helper starts on demand, only when a feature needs admin rights.
- Features share the host's UI thread, so work there stays within a few milliseconds and never waits for the disk, the network or another program. Follow [Threads and processes](docs/architecture/features.md#threads-and-processes); a feature gets its own process only for the reasons listed there.
- Identifiers: project and namespace prefix `PCCompanion`; executables `PCCompanion.exe` (settings app) and `PCCompanion.Host.exe` (host); data folder `%LOCALAPPDATA%\PCCompanion`. The user-visible name is "PC Companion".
- Keep dependencies one-way, with no cycles. Shared projects may be referenced by many projects. Keep OS-specific code isolated.
- Create projects only when first needed. No empty projects, speculative abstractions or unnecessary boilerplate.
- Never copy code from GPL-licensed projects (for example KDE, AltSnap, CopyQ or Rainmeter), including close ports or translations. Study their behavior only.

## Windows compatibility

- Supported: Windows 11 and Windows 10 version 2004 (build 19041) or later. The minimum version is `10.0.19041.0`; guard newer APIs with version checks (analyzer CA1416 flags unguarded calls).
- Undocumented Windows APIs are allowed only behind version-specific implementations that disable the feature on unknown Windows builds.
- Handle unsupported functionality gracefully, keep incompatible features from affecting other modules, and document each feature's OS requirements.

## UI

- WinUI 3 (Windows App SDK). Navigation must be easy, with clear labels and descriptions.
- Use our own icon; no Microsoft logos or look-alike branding.
- Support light and dark themes, keyboard navigation, accessibility, high DPI, multiple monitors and right-to-left layouts.

## Localization

- PC Companion supports any language. English is the source and fallback language. No language gets special treatment, and adding a language must not need code changes.
- No hardcoded user-facing strings. Strings live in `.resx` resources in a shared localization project; plurals use Windows' built-in ICU.
- Follow Microsoft conventions: BCP-47 tags, neutral-culture resources (`zh-Hans` for Simplified Chinese), formatting from the user's Windows regional settings, Microsoft terminology and style guides. Right-to-left layouts must work properly.
- Mark agent-drafted translations "needs native review". Logs and diagnostics stay in English; user-facing errors are localized.

## Security and privacy

- Least privilege: run without admin rights and request elevation only for operations that need it. Offer an optional "Always run as administrator" mode.
- No telemetry, analytics, accounts or automatic uploads. The app connects to the network only when a feature or updates need it, and makes no unnecessary connections. Each connection must be approved first and disclosed in `PRIVACY.md`.
- Treat external input (paths, clipboard content, commands, imported settings) as untrusted. Start processes with `ProcessStartInfo.ArgumentList`, never through a shell. Handle path traversal and symbolic links safely.
- Never hardcode secrets or store credentials in plain text; use Credential Manager or DPAPI. Never commit signing keys.
- Prefer user-mode alternatives to drivers.
- Every feature includes a proportionate security and privacy review.

## Logging

- Use Serilog through `Microsoft.Extensions.Logging`.
- Never log passwords, tokens or secrets. Avoid full file contents, minimize personal identifiers and sensitive paths, and redact where needed.

## Testing

- Automate testing as much as practical, and run the applicable validation (build, tests) for every task.
- Tests use isolated data and never touch windows or files they didn't create.
- Ask before UI tests that move the mouse or take focus, and before disruptive, elevated or system-changing tests (use Windows Sandbox or a VM). Don't install software without approval.
- `scripts/sandbox-test.ps1` runs the installer, settings app and host checks in Windows Sandbox, in groups: `-Checks <Group>` runs only that group after a short smoke check, and no `-Checks` runs every group. [Testing](docs/development/testing.md) lists the groups and which ones a change needs. Extend the feature's group in `scripts/sandbox/groups`, or add one, when a feature adds runtime behavior worth checking.
- Report what was and wasn't tested. Never claim that a test passed if you didn't run it.

## Git and GitHub

- Commit messages follow Conventional Commits and reference the GitHub issue when there is one, for example `feat(<area>): <summary> (#<issue>)`.
- Each commit is one logical unit and leaves the solution building.
- Each task gets its own short branch from an up-to-date `main`, named `<type>/<topic>` (for example `docs/pr-workflow`). The agent creates the branch when the task is authorized; the maintainer commits, pushes and opens the pull request unless they ask the agent to.
- `main` is protected. Pull requests merge with "Rebase and merge" once the required build checks pass and the branch is up to date with `main`.
- Work flows from idea to issue, then through the project board (Backlog, Evaluating, Approved) into tasks. The maintainer manages the board.
