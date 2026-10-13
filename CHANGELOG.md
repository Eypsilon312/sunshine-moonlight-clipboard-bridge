# Changelog

## v0.1.0-beta.1

First public preview of Clipboard Bridge for Windows.

- Bidirectional plain-text, file, and folder clipboard sharing over a LAN.
- Authenticated encrypted transport with per-session keys and replay checks.
- Explorer Ctrl+V support after received files have been staged locally.
- Automatic reconnection following connection loss.
- Optional startup at Windows sign-in with automatic connection.
- DPAPI-protected saved settings and pairing key.
- English interface, system-tray support, and custom embedded app icon.
- Automated protocol, archive, and settings tests.

This is an independent experimental companion for Sunshine/Moonlight. Full testing with two physical PCs and Moonlight remains outstanding. File transfers complete before pasting and do not resume after disconnection.
