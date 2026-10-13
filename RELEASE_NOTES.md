# Clipboard Bridge v0.1.0-beta.1 - Windows public preview

Share plain text, files, and folders between your Sunshine host and Moonlight client using Ctrl+C / Ctrl+V. Run this standalone companion on both Windows PCs; no Sunshine or Moonlight modifications are needed.

## Download and run

Download **ClipboardBridge-v0.1.0-beta.1-Windows.zip**, extract it on both PCs, and run `ClipboardBridge.exe`. Requires Windows 10 / 11 with .NET Framework 4.8. Set one PC to Host and the other to Client, enter the host LAN IP, and use the same generated pairing key on both PCs.

Includes an English interface, automatic reconnection, optional Windows sign-in startup, protected saved settings, and a custom app icon. `SHA256SUMS.txt` contains the ZIP and EXE SHA-256 checksums.

## Before trying this beta

- This is an independent community workaround, not an official Sunshine/Moonlight feature.
- Use on a trusted LAN. The encrypted protocol has no external security audit; do not expose the listening port to the internet.
- Files must finish transferring before you paste. Wait for **Files received - ready for Ctrl+V in Explorer** on the receiving PC.
- The executable is unsigned. Windows may show a SmartScreen notice.
- Automated transport, archive, and settings tests pass. The complete two-PC Moonlight workflow is still awaiting real-device verification.
- Images, rich text, virtual files, cut/move semantics, and resuming interrupted transfers are not supported.

Please report setup results and problems through the repository's Issues tab. Do not share pairing keys or private clipboard data.
