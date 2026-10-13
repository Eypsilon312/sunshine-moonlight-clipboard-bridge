# Clipboard Bridge v0.1.0-beta.3 - Windows public preview

This update adds automatic received-file cache cleanup after **three hours**, checked at startup and every minute. Files still in the clipboard or being sent are retained so pasting keeps working. Your original files and already pasted destination copies are never removed by this cleanup. Busy folders are retried later, and cleanup requires the app to be running.

This update adds **clipboard images and screenshots in both directions**. Capture with Win+Shift+S or copy an image, wait for **Image received - ready for Ctrl+V**, and paste into Paint, a document editor, or a chat app on the other PC. Images are transferred losslessly as encrypted PNG chunks and restored as a Windows clipboard bitmap. They do not create received-file cache entries.

**Update both PCs to beta 3 or later** for image sharing. Maximum image size: 32 MiB of encoded PNG, 40 megapixels, and 16,384 pixels per side. Pasting an image into Explorer does not automatically create a file.

Share plain text, images, files, and folders between your Sunshine host and Moonlight client using Ctrl+C / Ctrl+V. Run this standalone companion on both Windows PCs; no Sunshine or Moonlight modifications are needed.

## Download and run

Download **ClipboardBridge-v0.1.0-beta.3-Windows.zip**, extract it on both PCs, and run `ClipboardBridge.exe`. Requires Windows 10 / 11 with .NET Framework 4.8. Set one PC to Host and the other to Client, enter the host LAN IP, and use the same generated pairing key on both PCs.

Includes an English interface, automatic reconnection, optional Windows sign-in startup, protected saved settings, and a custom app icon. `SHA256SUMS.txt` contains the ZIP and EXE SHA-256 checksums.

## Before trying this beta

- This is an independent community workaround, not an official Sunshine/Moonlight feature.
- Use on a trusted LAN. The encrypted protocol has no external security audit; do not expose the listening port to the internet.
- Files must finish transferring before you paste. Wait for **Files received - ready for Ctrl+V in Explorer** on the receiving PC.
- The executable is unsigned. Windows may show a SmartScreen notice.
- Automated transport, archive, and settings tests pass. The complete two-PC Moonlight workflow is still awaiting real-device verification.
- Rich text, virtual files, cut/move semantics, and resuming interrupted transfers are not supported.
- Bidirectional multipart-image transfer and lossless pixel roundtrip tests pass. Native screenshots and application paste on two physical PCs still need manual verification.

Please report setup results and problems through the repository's Issues tab. Do not share pairing keys or private clipboard data.
