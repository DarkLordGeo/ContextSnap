# Releasing ContextSnap

The source repository (`ContextSnap`) stays private. Release packages are published as GitHub
Releases in a separate **public** repository, `ContextSnap-Releases`, and the website links to those
assets. No binaries are committed to the source repository.

## What the Release workflow does

`.github/workflows/release.yml` runs on a `v*` tag push or manually (Actions → Release → Run workflow,
with a version such as `1.0.0`).

| Job | Runs on | Permissions | What it does |
|---|---|---|---|
| `version` | Ubuntu | read | Validates the version (`1.2.3` or `1.2.3-beta.1`). |
| `windows` | Windows | read | `dotnet test`, `dotnet publish`, packages `ContextSnap-<v>-windows-x64.zip` + `.sha256`. |
| `macos` | macOS 15 | read | `swift test`, `scripts/build.sh`, packages `ContextSnap-<v>-macos-universal.dmg` and `.zip` + `.sha256`. |
| `publish` | Ubuntu | none (uses `RELEASES_TOKEN`) | Creates release `v<v>` in the releases repository with all packages and checksums (or replaces the assets if it exists), then downloads the ZIP and DMG **anonymously** from their public URLs and compares SHA-256 with the built files. Fails if either is not publicly downloadable or differs. |
| `update-website` | Ubuntu | `contents: write`, `pull-requests: write` on this repository | Writes the verified asset URLs, sizes, and SHA-256 into `website/downloads.json` on branch `release/v<v>-downloads` and opens a pull request. Nothing is pushed to the default branch. |

Merging that pull request deploys the website through the Vercel Git integration. Until then, and
whenever `downloads.json` lists no file, the download buttons stay disabled. The website only
enables a button for an `https://github.com/<owner>/<repo>/releases/download/<tag>/<file>` URL.

## One-time setup

1. **Create the public releases repository** `ContextSnap-Releases` under the same owner
   (for example `DarkLordGeo/ContextSnap-Releases`). Initialize it with a README so it has a default
   branch for release tags. It holds no source code.
   - A different name or owner: set the repository variable `RELEASES_REPO` (Settings → Secrets and
     variables → Actions → Variables) to `owner/name`.
2. **Create a fine-grained personal access token** (GitHub → Settings → Developer settings →
   Fine-grained tokens):
   - Repository access: **Only select repositories** → `ContextSnap-Releases`.
   - Repository permissions: **Contents: Read and write** (Metadata: read is added automatically).
     Nothing else.
   - Choose an expiration and note when to rotate it.
3. **Store it as a secret** in the private `ContextSnap` repository: Settings → Secrets and
   variables → Actions → New repository secret, name `RELEASES_TOKEN`. Never commit it.
4. **Allow the workflow to open pull requests**: Settings → Actions → General → Workflow
   permissions → enable **Allow GitHub Actions to create and approve pull requests**. (Without it the
   branch is still pushed and the workflow logs a warning; open the PR by hand.)
5. **Vercel**: the project's Root Directory must be `website`, with no build or install command.

## Publishing a release

1. Make sure CI is green on the default branch.
2. Run Actions → Release → Run workflow on the default branch with the version (for example `1.0.0`),
   or push a tag: `git tag v1.0.0 && git push origin v1.0.0`.
3. Wait for `publish` to pass; its log lists each verified public URL.
4. Review and merge the pull request "Website downloads: ContextSnap <version>".
5. On the deployed site, check that both buttons are enabled and download the files.

Re-running the workflow for an existing version replaces that release's assets and opens or updates
the same pull request branch.

## Notes

- Pull requests opened with `GITHUB_TOKEN` do not trigger other workflows such as CI. The Vercel
  preview still builds. The packages were already tested in the same Release run.
- Windows builds are framework-dependent (.NET 10 Desktop Runtime) and not code-signed. macOS builds
  are ad-hoc signed and not notarized. Set `MACOS_SIGNING_IDENTITY` in `scripts/build.sh`'s
  environment once a Developer ID certificate is available.
