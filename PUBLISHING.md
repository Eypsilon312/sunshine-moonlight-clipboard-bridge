# Publishing to GitHub

Target: `Eypsilon312/sunshine-moonlight-clipboard-bridge`. First public preview: `v0.1.0-beta.2`.

## Recommended release shape

Publish source in a public repository under the MIT license, with an English README, a screenshot, an Issues template, and a versioned Windows ZIP attached to a **prerelease**. The repository includes an automated Windows build/test workflow. Keep binaries in release assets; `dist` is excluded from Git. No settings, pairing keys, or received files should be committed.

## Publish with your account

Run from this project folder in PowerShell:

```powershell
.\setup-github.ps1 -Login
.\publish.ps1
```

The setup script downloads the official portable GitHub CLI for Windows x64 and verifies its checksum before extracting `gh.exe` into `dist\tools`. Sign in in the browser as **Eypsilon312**. Never paste a GitHub token into chat or source files. The publication script verifies the authenticated account, builds and tests the app, creates the public repository if necessary, initializes Git, commits the project, pushes `main` and the version tag, and creates a public prerelease with the ZIP and checksums. It uses an account-scoped noreply commit email. Existing remotes, tags, repository visibility, and release assets are not silently replaced.

If you have an installed GitHub CLI, `publish.ps1` uses it. If its login lacks workflow access, use `gh auth refresh -h github.com -s workflow` before pushing `.github/workflows`. Signing in requires your own GitHub account authorization; preparing the project does not publish it.

## Alternative: GitHub web interface

1. Create the public repository `sunshine-moonlight-clipboard-bridge` under Eypsilon312, without initializing a second README or license.
2. Upload the prepared source files, `assets`, `docs`, and `.github` folders. Exclude `dist`, settings files, keys, and received files. A Git client is preferable for preserving the full folder structure.
3. In **Releases**, choose **Draft a new release**, create tag `v0.1.0-beta.2`, and use the text from `RELEASE_NOTES.md`.
4. Attach `dist\ClipboardBridge-v0.1.0-beta.2-Windows.zip` and `dist\SHA256SUMS.txt`, check **Set as a pre-release**, and publish.

## Introduce it to the community

Share the repository/release link in a relevant Moonlight or Sunshine community support/discussion channel after checking its posting rules. Do not advertise through unrelated bug reports or imply endorsement. A useful introduction states the supported Windows setup, shows the copy/paste workflow, explains the file-transfer wait, and asks for real-device feedback. This project has not automatically posted to any community.

Suggested English announcement:

> I made an experimental Windows clipboard companion for Sunshine and Moonlight. It runs on both PCs alongside your existing installs and shares plain text, files, and folders in both directions using Ctrl+C / Ctrl+V. It includes encrypted LAN transport, automatic reconnection, and optional Windows sign-in startup.
>
> Files need to finish transferring before you paste; it is not RDP's on-demand transfer. This is an independent community workaround, not an official Sunshine/Moonlight feature. The first beta has automated tests, but I am looking for feedback from real two-PC setups. The executable is unsigned and the custom protocol has not had an external security audit. Intended for trusted LAN use.
>
> Source and Windows download: https://github.com/Eypsilon312/sunshine-moonlight-clipboard-bridge/releases

Run the checklist in `TESTING.md` on your two PCs before describing the beta as verified with Moonlight.

Official references: [GitHub CLI sign-in](https://cli.github.com/manual/gh_auth_login), [create a repository](https://cli.github.com/manual/gh_repo_create), and [create a release](https://cli.github.com/manual/gh_release_create).
