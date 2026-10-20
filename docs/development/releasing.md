# Releasing

How a version of PC Companion goes from `main` to a published GitHub release, and how hotfixes are made. The decisions behind it are in [#80](https://github.com/Lazyb0y/pc-companion/issues/80), [#81](https://github.com/Lazyb0y/pc-companion/issues/81) and [#82](https://github.com/Lazyb0y/pc-companion/issues/82).

## Versions

- Semantic versioning, `0.MINOR.PATCH` during the preview: a release raises MINOR and a hotfix raises PATCH.
- Every release must be higher than the last. The installer upgrades only to a higher version, and the [update check](../features/general/update-check.md) reads release tags as `vMAJOR.MINOR.PATCH`.
- The version is `Version` in `Directory.Build.props`. It has three parts, the ones Windows Installer compares; the files' informational version adds the commit, as in `0.1.0+<sha>`.
- Tags look like `v0.1.0`, and titles like "PC Companion 0.1.0 Preview" during 0.x. Releases aren't marked as pre-releases, so `/releases/latest`, which the update check asks for, finds them.

## Making a release

1. Raise `Version` in `Directory.Build.props` in a pull request to `main`, and merge it.
2. Merge `main` into the `release` branch and push it. Only administrators can push to `release`, and rulesets block deleting it and force pushes.

   ```
   git switch release
   git pull --ff-only
   git merge origin/main
   git push
   ```

3. The push runs the release workflow, `.github/workflows/release.yml`:
   - It stops if the version isn't `MAJOR.MINOR.PATCH`, is already published, or isn't higher than the latest release.
   - It runs the same builds, tests and installers as `main`, by calling `build.yml`.
   - It creates the draft release `v<version>` on the pushed commit, with `PCCompanion-<version>-x64.msi`, `PCCompanion-<version>-ARM64.msi`, `SHA256SUMS.txt` and a build provenance attestation for each.
   - The notes come from `.github/release-notes.md`: the installers with their hashes and how to check them, how to install and uninstall, and where data is kept, followed by GitHub's list of the pull requests since the last release.
   - Only collaborators see drafts. Pushing `release` again replaces the draft and its notes, so rewrite the notes after the last push.
4. Check the draft's own files before publishing:
   - Download them: `gh release download v0.1.0 --dir <folder>`.
   - Compare each installer's `Get-FileHash` with `SHA256SUMS.txt`.
   - Check each file's attestation: `gh attestation verify <file> -R Lazyb0y/pc-companion`.
   - Run `scripts\verify.ps1` on the pushed commit.
   - Run every Sandbox group on the downloaded installer: `scripts\sandbox-test.ps1 -Msi <folder>\PCCompanion-0.1.0-x64.msi`; see [Testing](testing.md#testing-in-windows-sandbox).
   - By hand, once: install it, upgrade to it from the previous release, and uninstall it.
5. Rewrite the generated list of pull requests into these sections, and remove the comment at the top:
   - highlights;
   - a section for each feature, with its changes and fixes, each a sentence that ends with its pull request, such as "Added Keep displays on in #77";
   - development: CI, tests, documentation and other changes users don't see;
   - known issues;
   - thanks to contributors from outside the project.
6. Publish the draft, as the latest release. Publishing creates the tag `v<version>` on the draft's commit, and installed copies of PC Companion find the new version at their next daily update check.
7. If the release adds or changes features, update the [website](website.md) in a pull request to `main`, with [screenshots](website.md#screenshots) taken from the release's installer, and merge it once the release is published, so the site never describes features the download doesn't have.

## Hotfixes

- A hotfix is for a problem in the latest release with no reasonable workaround: a crash or hang, data loss, a security vulnerability, or an install, upgrade or uninstall that fails or leaves things behind. Anything else waits for the next release.
- The fix lands on `main` through a pull request as usual. Cherry-pick it onto `release`, raise PATCH there, and push: the workflow drafts the hotfix, which gets the same checks.
- The next merge of `main` into `release` conflicts on the `Version` line; keep `main`'s version.
- Never change a published release's files. If something is wrong with a release, publish a fixed version.
