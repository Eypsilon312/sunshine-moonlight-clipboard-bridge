# Manual verification checklist

Use two Windows PCs running Sunshine and Moonlight. These checks require a real two-PC setup; automated tests do not establish that they pass.

- [ ] Pair both apps on a private LAN and confirm Connected.
- [ ] Copy Unicode text and multiline text client to host; paste in the host's text editor.
- [ ] Copy different text host to client; paste in the local client's text editor.
- [ ] Transfer one file in each direction; paste into a chosen Explorer folder and compare SHA-256 hashes.
- [ ] Transfer a folder with nested files, Unicode names, and an empty folder; compare the results.
- [ ] Transfer a larger file and confirm progress and ready status before pasting.
- [ ] Copy something new on the receiver during a file transfer; confirm the newer local clipboard is preserved and files remain in Received files.
- [ ] Stop synchronization; confirm new copies are not sent.
- [ ] Minimize and restore the app from the system tray.
- [ ] Restart the app; confirm saved role, IP, port, key, and file setting load correctly.
- [ ] Enable startup on both PCs, reboot the host, sign in, and confirm automatic reconnection without manually starting the host app.
- [ ] Interrupt the network mid-transfer; confirm incomplete files are not published to the clipboard. Reconnect and copy again.
- [ ] Disable startup and confirm the ClipboardBridge Run entry is removed.
- [ ] Keep a received file in the clipboard for more than three hours: pasting must still work. Copy unrelated text, wait for the next minute sweep, and confirm the expired transfer cache is removed but the pasted destination copy remains.

Report Windows versions, app version, Moonlight/Sunshine versions, transfer direction, and relevant status messages. Do not include pairing keys or sensitive clipboard data.
