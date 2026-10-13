# Changelog

## v0.1.0-beta.3

- Share clipboard images and screenshots in both directions.
- Transfer images losslessly as PNG using authenticated encrypted chunks, without received-file cache entries.
- Restore images in the standard Windows clipboard bitmap format for pasting into image-capable apps.
- Preserve newer local clipboard contents during image reception and suppress synchronization echoes.
- Limit images to 32 MiB of encoded PNG, 40 megapixels, and 16,384 pixels per side.
- Add bidirectional multipart-image transport, pixel-fidelity, invalid-image, and incomplete-transfer tests.

Both PCs must be updated for image sharing; older versions cannot receive image messages. Native screenshot-to-app paste tests on two physical PCs remain outstanding.

## v0.1.0-beta.2

- Automatically remove received-file cache folders after three hours.
- Retain files referenced by the current clipboard, queued or active outgoing transfers, and active receptions.
- Sweep on startup and once per minute; retry busy folders later.
- Add cache-expiry and clipboard-retention tests.

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
