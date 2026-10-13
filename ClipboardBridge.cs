using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClipboardBridge
{
    static class Program
    {
        [STAThread] static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") { SelfTest.Run(); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--ui-test")
            {
                using (var form = new BridgeForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000); form.ShowInTaskbar = false;
                    form.Show(); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-preview.png"));
                    }
                }
                return;
            }
            bool created;
            using (var single = new Mutex(true, "Local\\ClipboardBridge.Singleton", out created))
            {
                if (!created) { MessageBox.Show("Clipboard Bridge is already running. Open it from the system tray."); return; }
                Application.Run(new BridgeForm(args.Length > 0 && args[0] == "--autostart"));
            }
        }
    }

    // Protocol: authenticated nonce exchange, session-specific encryption/MAC keys,
    // then AES-256-CBC encrypt-then-HMAC-SHA256 frames with ordered counters.
    sealed class Channel : IDisposable
    {
        const int MaxFrame = 1024 * 1024;
        readonly Stream stream;
        readonly byte[] enc, mac;
        readonly byte sendDirection, receiveDirection;
        ulong sendCounter, receiveCounter;
        public Channel(Stream stream, byte[] secret, bool server)
        {
            this.stream = stream;
            byte[] local = RandomBytes(32), remote;
            if (server) { remote = ReadExact(stream, 32); stream.Write(local, 0, 32); }
            else { stream.Write(local, 0, 32); remote = ReadExact(stream, 32); }
            byte[] context = Join(Encoding.ASCII.GetBytes("ClipboardBridge/1"), server ? remote : local, server ? local : remote);
            byte[] proof = Hash(secret, Join(context, new byte[] { server ? (byte)1 : (byte)0 }));
            stream.Write(proof, 0, proof.Length);
            byte[] peerProof = ReadExact(stream, 32);
            if (!Equal(peerProof, Hash(secret, Join(context, new byte[] { server ? (byte)0 : (byte)1 }))))
                throw new IOException("Pairing keys do not match.");
            enc = Hash(secret, Join(context, Encoding.ASCII.GetBytes("encryption")));
            mac = Hash(secret, Join(context, Encoding.ASCII.GetBytes("authentication")));
            sendDirection = server ? (byte)1 : (byte)0;
            receiveDirection = server ? (byte)0 : (byte)1;
        }
        public void Send(byte kind, byte[] payload)
        {
            byte[] plain = Join(new byte[] { kind }, payload);
            if (plain.Length > MaxFrame) throw new IOException("Message is too large.");
            byte[] iv = RandomBytes(16), cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Key = enc; aes.IV = iv;
                using (ICryptoTransform t = aes.CreateEncryptor()) cipher = t.TransformFinalBlock(plain, 0, plain.Length);
            }
            byte[] counter = BitConverter.GetBytes(sendCounter++);
            byte[] body = Join(counter, iv, cipher);
            byte[] tag = Hash(mac, Join(new byte[] { sendDirection }, body));
            byte[] length = BitConverter.GetBytes(body.Length + tag.Length);
            stream.Write(length, 0, 4); stream.Write(body, 0, body.Length); stream.Write(tag, 0, tag.Length);
        }
        public byte[] Receive()
        {
            int length = BitConverter.ToInt32(ReadExact(stream, 4), 0);
            if (length < 72 || length > MaxFrame + 88) throw new IOException("Invalid message size.");
            byte[] packet = ReadExact(stream, length);
            byte[] body = Slice(packet, 0, length - 32), tag = Slice(packet, length - 32, 32);
            if (!Equal(tag, Hash(mac, Join(new byte[] { receiveDirection }, body)))) throw new IOException("Message authentication failed.");
            if (BitConverter.ToUInt64(body, 0) != receiveCounter++) throw new IOException("Invalid message sequence.");
            using (Aes aes = Aes.Create())
            {
                aes.Key = enc; aes.IV = Slice(body, 8, 16);
                using (ICryptoTransform t = aes.CreateDecryptor()) return t.TransformFinalBlock(body, 24, body.Length - 24);
            }
        }
        public void Dispose() { stream.Dispose(); }
        public static byte[] RandomBytes(int n) { byte[] b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
        public static byte[] Hash(byte[] key, byte[] value) { using (var h = new HMACSHA256(key)) return h.ComputeHash(value); }
        public static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }
        public static byte[] Join(params byte[][] parts) { int n = 0; foreach (byte[] p in parts) n += p.Length; byte[] b = new byte[n]; int o = 0; foreach (byte[] p in parts) { Buffer.BlockCopy(p, 0, b, o, p.Length); o += p.Length; } return b; }
        public static byte[] Slice(byte[] b, int o, int n) { byte[] r = new byte[n]; Buffer.BlockCopy(b, o, r, 0, n); return r; }
        public static byte[] ReadExact(Stream s, int n) { byte[] b = new byte[n]; int o = 0; while (o < n) { int r = s.Read(b, o, n - o); if (r == 0) throw new EndOfStreamException(); o += r; } return b; }
    }

    static class Files
    {
        public const long MaxBytes = 10L * 1024 * 1024 * 1024;
        const int MaxEntries = 100000;
        public static string BaseDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge", "Received"); } }
        public static string Pack(string[] paths) { return Pack(paths, delegate { }); }
        public static string Pack(string[] paths, Action pulse)
        {
            string temp = Path.Combine(Path.GetTempPath(), "ClipboardBridge-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                long total = 0; int count = 0;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    foreach (string path in paths)
                    {
                        pulse();
                        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                        string name = Path.GetFileName(full);
                        if (String.IsNullOrEmpty(name) || !names.Add(name)) throw new IOException("Duplicate top-level names and drive roots are not supported.");
                        Add(zip, full, name, ref total, ref count, pulse);
                    }
                }
                if (new FileInfo(temp).Length > MaxBytes) throw new IOException("File transfer exceeds 10 GiB.");
                return temp;
            }
            catch { File.Delete(temp); throw; }
        }
        static void Add(ZipArchive zip, string path, string name, ref long total, ref int count, Action pulse)
        {
            pulse();
            if (++count > MaxEntries) throw new IOException("Too many files.");
            FileAttributes attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders (reparse points) cannot be transferred.");
            if ((attr & FileAttributes.Directory) != 0)
            {
                zip.CreateEntry(name + "/");
                foreach (string child in Directory.EnumerateFileSystemEntries(path)) Add(zip, child, name + "/" + Path.GetFileName(child), ref total, ref count, pulse);
            }
            else
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using (var input = File.OpenRead(path)) using (var output = entry.Open())
                {
                    byte[] buffer = new byte[262144]; int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        pulse(); total += n;
                        if (total > MaxBytes) throw new IOException("File transfer exceeds 10 GiB.");
                        output.Write(buffer, 0, n);
                    }
                }
            }
        }
        public static string SafePath(string root, string name)
        {
            string[] parts = name.Replace('\\', '/').Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0 && i == parts.Length - 1) continue;
                if (p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith(".") || p.EndsWith(" ")) throw new IOException("Invalid file path.");
                string stem = p.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '0' && stem[3] <= '9')) throw new IOException("Reserved file name.");
            }
            string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path is outside the received-files folder.");
            return target;
        }
        public static string[] Unpack(string archive)
        {
            string root = Path.Combine(BaseDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                using (var zip = ZipFile.OpenRead(archive))
                {
                    if (zip.Entries.Count > MaxEntries) throw new IOException("Too many archive entries.");
                    foreach (var entry in zip.Entries)
                    {
                        string target = SafePath(root, entry.FullName);
                        if (!entries.Add(target.TrimEnd(Path.DirectorySeparatorChar))) throw new IOException("Duplicate archive entry.");
                        total += entry.Length;
                        if (total > MaxBytes) throw new IOException("Archive exceeds 10 GiB.");
                        string top = entry.FullName.Replace('\\', '/').Split('/')[0];
                        roots.Add(Path.Combine(root, top));
                        if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew))
                        {
                            byte[] buffer = new byte[65536]; long written = 0; int n;
                            while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { written += n; if (written > entry.Length) throw new IOException("Invalid archive size."); output.Write(buffer, 0, n); }
                            if (written != entry.Length) throw new IOException("Incomplete file.");
                        }
                    }
                }
                if (roots.Count == 0) throw new IOException("Empty archive.");
                string[] result = new string[roots.Count]; roots.CopyTo(result); return result;
            }
            catch { Directory.Delete(root, true); throw; }
        }
    }

    sealed class Settings
    {
        public int Role, Port = 47995;
        public string Address = "", Key = "";
        public bool AutoStart, SyncFiles = true;
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge", "settings.dat"); } }
        public static string StartupCommand(string executable) { return "\"" + executable + "\" --autostart"; }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                { writer.Write(1); writer.Write(Role); writer.Write(Port); writer.Write(Address); writer.Write(Key); writer.Write(AutoStart); writer.Write(SyncFiles); }
                bytes = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);
            }
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static Settings Load(string path)
        {
            if (!File.Exists(path)) return new Settings();
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                if (reader.ReadInt32() != 1) throw new IOException("Unknown settings version.");
                var s = new Settings { Role = reader.ReadInt32(), Port = reader.ReadInt32(), Address = reader.ReadString(), Key = reader.ReadString(), AutoStart = reader.ReadBoolean(), SyncFiles = reader.ReadBoolean() };
                if (s.Role < 0 || s.Role > 1 || s.Port < 1024 || s.Port > 65535) throw new IOException("Invalid settings.");
                return s;
            }
        }
        public static void SetAutoStart(bool enabled)
        {
            using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) run.SetValue("ClipboardBridge", StartupCommand(Application.ExecutablePath), RegistryValueKind.String);
                else run.DeleteValue("ClipboardBridge", false);
            }
        }
    }

    sealed class Snapshot { public string Text; public string[] Paths; }

    sealed class BridgeForm : Form
    {
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        readonly ComboBox role = new ComboBox();
        readonly TextBox address = new TextBox(), key = new TextBox();
        readonly NumericUpDown port = new NumericUpDown();
        readonly Button start = new Button(), stop = new Button(), generate = new Button();
        readonly CheckBox syncFiles = new CheckBox();
        readonly CheckBox autoStart = new CheckBox();
        bool loadingSettings;
        readonly Label status = new Label();
        readonly TextBox log = new TextBox();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly AutoResetEvent changed = new AutoResetEvent(false);
        readonly object gate = new object();
        readonly NotifyIcon tray = new NotifyIcon();
        volatile bool running, connected;
        int connectionId;
        TcpListener listener;
        TcpClient client;
        Thread worker;
        Snapshot pending;
        uint sequence;
        DateTime nextRetry;
        public BridgeForm() : this(false) { }
        public BridgeForm(bool startup)
        {
            Text = "Clipboard Bridge · Sunshine / Moonlight"; ClientSize = new Size(660, 530); MinimumSize = Size;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 10);
            AddLabel("Device", 20, 20); role.SetBounds(180, 16, 450, 28); role.DropDownStyle = ComboBoxStyle.DropDownList; role.Items.AddRange(new object[] { "Host - accept connections", "Client - connect to host" }); role.SelectedIndex = 0; Controls.Add(role);
            AddLabel("Host IP (on client)", 20, 60); address.SetBounds(180, 56, 450, 28); Controls.Add(address);
            AddLabel("Port", 20, 100); port.SetBounds(180, 96, 100, 28); port.Minimum = 1024; port.Maximum = 65535; port.Value = 47995; Controls.Add(port);
            AddLabel("Pairing key", 20, 140); key.SetBounds(180, 136, 450, 28); key.UseSystemPasswordChar = true; Controls.Add(key);
            generate.Text = "Generate and copy key"; generate.SetBounds(180, 174, 270, 32); generate.Click += delegate { key.Text = Convert.ToBase64String(Channel.RandomBytes(32)); Clipboard.SetText(key.Text); MessageBox.Show("Key copied. Paste it into the pairing key field on the other device. Copy something else before starting synchronization.", "Pairing"); }; Controls.Add(generate);
            syncFiles.Text = "Synchronize files and folders (up to 10 GiB)"; syncFiles.Checked = true; syncFiles.SetBounds(20, 219, 610, 28); Controls.Add(syncFiles);
            start.Text = "Start"; start.SetBounds(20, 262, 130, 34); start.Click += delegate { StartBridge(); }; Controls.Add(start);
            stop.Text = "Stop"; stop.Enabled = false; stop.SetBounds(160, 262, 130, 34); stop.Click += delegate { StopBridge(); }; Controls.Add(stop);
            var open = new Button { Text = "Received files" }; open.SetBounds(310, 262, 170, 34); open.Click += delegate { Directory.CreateDirectory(Files.BaseDirectory); System.Diagnostics.Process.Start("explorer.exe", Files.BaseDirectory); }; Controls.Add(open);
            status.SetBounds(20, 310, 610, 28); status.Text = "Ready. Use the same pairing key on both devices."; Controls.Add(status);
            autoStart.Text = "Start at Windows sign-in and connect automatically";
            autoStart.SetBounds(20, 345, 610, 28); Controls.Add(autoStart);
            log.SetBounds(20, 385, 610, 120); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; Controls.Add(log);
            timer.Interval = 150; timer.Tick += delegate { PollClipboard(); }; timer.Start();
            tray.Icon = Icon; tray.Text = "Clipboard Bridge"; tray.Visible = true;
            var menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); }); menu.Items.Add("Exit", null, delegate { Close(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate { StopBridge(); timer.Stop(); tray.Dispose(); };
            loadingSettings = true;
            try
            {
                Settings saved = Settings.Load(Settings.FilePath);
                role.SelectedIndex = saved.Role; address.Text = saved.Address; port.Value = saved.Port; key.Text = saved.Key;
                syncFiles.Checked = saved.SyncFiles; autoStart.Checked = saved.AutoStart;
            }
            catch (Exception ex) { status.Text = "Could not load settings: " + ex.Message; }
            loadingSettings = false;
            autoStart.CheckedChanged += delegate
            {
                if (loadingSettings) return;
                try
                {
                    if (autoStart.Checked)
                    {
                        byte[] secret = Convert.FromBase64String(key.Text.Trim());
                        if (secret.Length != 32 || (role.SelectedIndex == 1 && String.IsNullOrWhiteSpace(address.Text))) throw new IOException("Enter a valid pairing key and host IP first.");
                    }
                    Settings.SetAutoStart(autoStart.Checked); SaveSettings();
                    Report(autoStart.Checked ? "Startup enabled - connects after Windows sign-in" : "Startup disabled");
                }
                catch (Exception ex)
                {
                    loadingSettings = true; autoStart.Checked = !autoStart.Checked; loadingSettings = false;
                    try { Settings.SetAutoStart(autoStart.Checked); SaveSettings(); } catch { }
                    MessageBox.Show(ex.Message, "Windows startup");
                }
            };
            syncFiles.CheckedChanged += delegate { try { SaveSettings(); } catch (Exception ex) { Report("Saving settings: " + ex.Message); } };
            Shown += delegate
            {
                if (startup && autoStart.Checked)
                {
                    StartBridge();
                    if (running) { WindowState = FormWindowState.Minimized; Hide(); }
                }
            };
        }
        void SaveSettings()
        {
            new Settings { Role = role.SelectedIndex, Port = (int)port.Value, Address = address.Text.Trim(), Key = key.Text.Trim(), AutoStart = autoStart.Checked, SyncFiles = syncFiles.Checked }.Save(Settings.FilePath);
        }
        void AddLabel(string text, int x, int y) { var l = new Label { Text = text }; l.SetBounds(x, y, 160, 28); Controls.Add(l); }
        void Ui(Action action) { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } }
        void Report(string message) { Ui(delegate { status.Text = message; log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); if (log.TextLength > 20000) log.Text = log.Text.Substring(log.TextLength - 10000); }); }
        void StartBridge()
        {
            byte[] secret;
            try { secret = Convert.FromBase64String(key.Text.Trim()); if (secret.Length != 32) throw new FormatException(); }
            catch { MessageBox.Show("Use a generated 32-byte pairing key."); return; }
            bool server = role.SelectedIndex == 0; string host = address.Text.Trim(); int number = (int)port.Value;
            if (!server && String.IsNullOrEmpty(host)) { MessageBox.Show("Enter the LAN IP address of the host."); return; }
            try { SaveSettings(); if (autoStart.Checked) Settings.SetAutoStart(true); }
            catch (Exception ex) { MessageBox.Show("Could not save settings: " + ex.Message); return; }
            running = true; sequence = GetClipboardSequenceNumber(); pending = null;
            start.Enabled = role.Enabled = address.Enabled = port.Enabled = key.Enabled = generate.Enabled = false; stop.Enabled = true;
            worker = new Thread(delegate() { Run(server, host, number, secret); }); worker.IsBackground = true; worker.Start();
        }
        void StopBridge()
        {
            running = false; connected = false; changed.Set();
            lock (gate) { if (client != null) client.Close(); if (listener != null) listener.Stop(); pending = null; }
            stop.Enabled = false;
            status.Text = "Disconnecting...";
            if (worker == null || !worker.IsAlive) Stopped();
        }
        void Stopped()
        {
            start.Enabled = role.Enabled = address.Enabled = port.Enabled = key.Enabled = generate.Enabled = true; stop.Enabled = false;
            status.Text = "Stopped. Clipboard synchronization is paused.";
        }
        void PollClipboard()
        {
            if (!running || !connected || DateTime.UtcNow < nextRetry) return;
            uint current = GetClipboardSequenceNumber(); if (current == sequence) return;
            try
            {
                Snapshot s = new Snapshot();
                if (Clipboard.ContainsFileDropList())
                {
                    if (!syncFiles.Checked) { sequence = current; return; }
                    StringCollection files = Clipboard.GetFileDropList(); s.Paths = new string[files.Count]; files.CopyTo(s.Paths, 0);
                }
                else if (Clipboard.ContainsText())
                {
                    s.Text = Clipboard.GetText();
                    // Never broadcast the pairing secret, even if it was just copied.
                    if (s.Text.Trim() == key.Text.Trim()) { sequence = current; return; }
                    if (Encoding.UTF8.GetByteCount(s.Text) > 900000) { sequence = current; Report("Text is too large (maximum 900 KB)."); return; }
                }
                else { sequence = current; return; }
                if (GetClipboardSequenceNumber() != current) return;
                sequence = current; lock (gate) pending = s; changed.Set();
            }
            catch (ExternalException) { nextRetry = DateTime.UtcNow.AddMilliseconds(300); }
        }
        void Run(bool server, string host, int number, byte[] secret)
        {
            try
            {
                if (server) { lock (gate) { listener = new TcpListener(IPAddress.Any, number); listener.Start(4); } }
                while (running)
                {
                    try
                    {
                        Report(server ? "Waiting for client - TCP " + number : "Connecting to " + host + "...");
                        TcpClient peer;
                        if (server) peer = listener.AcceptTcpClient();
                        else
                        {
                            peer = new TcpClient(); lock (gate) client = peer;
                            var result = peer.BeginConnect(host, number, null, null);
                            if (!result.AsyncWaitHandle.WaitOne(5000)) { peer.Close(); throw new IOException("Connection timed out."); }
                            peer.EndConnect(result); result.AsyncWaitHandle.Close();
                        }
                        lock (gate) { client = peer; if (!running) { peer.Close(); break; } }
                        peer.NoDelay = true; peer.ReceiveTimeout = 10000; peer.SendTimeout = 30000;
                        using (var channel = new Channel(peer.GetStream(), secret, server))
                        {
                            peer.ReceiveTimeout = 45000;
                            Ui(delegate { sequence = GetClipboardSequenceNumber(); });
                            Interlocked.Increment(ref connectionId);
                            connected = true; Report("Connected - clipboard synchronization active");
                            var sender = new Thread(delegate() { SendLoop(channel, peer); }); sender.IsBackground = true; sender.Start();
                            try { ReceiveLoop(channel); }
                            finally { connected = false; peer.Close(); changed.Set(); sender.Join(); lock (gate) pending = null; }
                        }
                    }
                    catch (Exception ex) { connected = false; lock (gate) { if (client != null) client.Close(); } if (running) Report("Connection: " + ex.Message); }
                    if (running) { for (int i = 0; i < 20 && running; i++) Thread.Sleep(100); }
                }
            }
            catch (Exception ex) { Report("Could not start: " + ex.Message); Ui(delegate { StopBridge(); }); }
            finally { running = false; lock (gate) { if (listener != null) { listener.Stop(); listener = null; } } Ui(delegate { Stopped(); }); }
        }
        void SendLoop(Channel channel, TcpClient peer)
        {
            try
            {
                while (running && connected)
                {
                    changed.WaitOne(10000);
                    if (!running || !connected) break;
                    Snapshot s; lock (gate) { s = pending; pending = null; }
                    if (s == null) { channel.Send((byte)'H', new byte[0]); continue; }
                    if (s.Text != null) { channel.Send((byte)'T', Encoding.UTF8.GetBytes(s.Text)); Report("Text sent."); continue; }
                    string archive = null;
                    try
                    {
                        Report("Preparing files...");
                        // Preparation errors do not break an otherwise healthy connection.
                        DateTime heartbeat = DateTime.UtcNow;
                        archive = Files.Pack(s.Paths, delegate
                        {
                            if (!running || !connected) throw new IOException("Transfer cancelled.");
                            if (DateTime.UtcNow >= heartbeat) { channel.Send((byte)'H', new byte[0]); heartbeat = DateTime.UtcNow.AddSeconds(10); }
                        });
                    }
                    catch (Exception ex) { Report("Files were not sent: " + ex.Message); continue; }
                    try
                    {
                        long size = new FileInfo(archive).Length;
                        channel.Send((byte)'B', BitConverter.GetBytes(size));
                        using (var input = File.OpenRead(archive))
                        {
                            byte[] buffer = new byte[262144]; int n; long sent = 0; DateTime update = DateTime.MinValue;
                            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (!running || !connected) throw new IOException("Transfer cancelled.");
                                channel.Send((byte)'D', Channel.Slice(buffer, 0, n)); sent += n;
                                if (DateTime.UtcNow >= update) { Report("Sending files: " + (sent * 100 / size) + " %"); update = DateTime.UtcNow.AddSeconds(1); }
                            }
                        }
                        channel.Send((byte)'E', new byte[0]); Report("Files sent. The receiver is preparing them for Ctrl+V.");
                    }
                    finally { File.Delete(archive); }
                }
            }
            catch (Exception ex) { if (running) Report("Sending: " + ex.Message); connected = false; peer.Close(); }
        }
        void ReceiveLoop(Channel channel)
        {
            string archive = null; FileStream output = null; long expected = 0, received = 0; uint before = 0; DateTime update = DateTime.MinValue;
            try
            {
                while (running && connected)
                {
                    byte[] frame = channel.Receive(); byte kind = frame[0];
                    if (kind == 'H') { if (frame.Length != 1) throw new IOException("Invalid heartbeat."); continue; }
                    if (kind == 'T')
                    {
                        if (output != null || frame.Length > 900001) throw new IOException("Invalid text message.");
                        string text = new UTF8Encoding(false, true).GetString(frame, 1, frame.Length - 1);
                        Apply(new Snapshot { Text = text }, GetClipboardSequenceNumber(), 0); continue;
                    }
                    if (kind == 'B')
                    {
                        if (output != null || frame.Length != 9) throw new IOException("Invalid transfer start.");
                        expected = BitConverter.ToInt64(frame, 1); if (expected <= 0 || expected > Files.MaxBytes) throw new IOException("Invalid file size.");
                        received = 0; before = GetClipboardSequenceNumber();
                        archive = Path.Combine(Path.GetTempPath(), "ClipboardBridge-" + Guid.NewGuid().ToString("N") + ".zip");
                        output = new FileStream(archive, FileMode.CreateNew); continue;
                    }
                    if (kind == 'D')
                    {
                        if (output == null || frame.Length < 2) throw new IOException("File data received without a transfer start.");
                        received += frame.Length - 1; if (received > expected) throw new IOException("Too much file data.");
                        output.Write(frame, 1, frame.Length - 1);
                        if (DateTime.UtcNow >= update) { Report("Receiving files: " + (received * 100 / expected) + " %"); update = DateTime.UtcNow.AddSeconds(1); }
                        continue;
                    }
                    if (kind == 'E')
                    {
                        if (output == null || received != expected || frame.Length != 1) throw new IOException("Incomplete transfer.");
                        output.Dispose(); output = null;
                        bool allowed = false; Invoke(new Action(delegate { allowed = syncFiles.Checked; }));
                        if (allowed) { Report("Preparing received files..."); string[] paths = Files.Unpack(archive); Apply(new Snapshot { Paths = paths }, before, 0); }
                        else Report("File reception is disabled; transfer discarded.");
                        File.Delete(archive); archive = null; continue;
                    }
                    throw new IOException("Unknown message.");
                }
            }
            finally { if (output != null) output.Dispose(); if (archive != null) File.Delete(archive); }
        }
        void Apply(Snapshot s, uint expectedSequence, int attempt)
        {
            int session = connectionId;
            Ui(delegate
            {
                if (!running || !connected || session != connectionId) return;
                if (GetClipboardSequenceNumber() != expectedSequence)
                {
                    Report(s.Paths == null ? "Local clipboard changed; received text was discarded." : "Local clipboard changed. Files are in the received-files folder."); return;
                }
                try
                {
                    if (s.Paths != null)
                    {
                        if (!syncFiles.Checked) return;
                        var list = new StringCollection(); list.AddRange(s.Paths); Clipboard.SetFileDropList(list);
                    }
                    else Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, s.Text), true);
                    sequence = GetClipboardSequenceNumber();
                    lock (gate) pending = null;
                    Report(s.Paths == null ? "Text received - ready for Ctrl+V" : "Files received - ready for Ctrl+V in Explorer");
                }
                catch (ExternalException)
                {
                    if (attempt >= 15) { Report("Clipboard is busy; could not prepare pasting."); return; }
                    var retry = new System.Windows.Forms.Timer { Interval = 200 };
                    retry.Tick += delegate { retry.Stop(); retry.Dispose(); if (session == connectionId) Apply(s, expectedSequence, attempt + 1); }; retry.Start();
                }
            });
        }
    }

    static class SelfTest
    {
        public static void Run()
        {
            string result = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt");
            try
            {
                TestProtocol(); TestFiles(); TestSettings();
                File.WriteAllText(result, "PASS: authenticated encrypted bidirectional frames; wrong-key rejection; tamper/replay rejection; Unicode/files/empty folders; path traversal and duplicate archive rejection; cancelled packing cleanup; DPAPI settings roundtrip and replacement; quoted autostart command.\r\n");
            }
            catch (Exception ex) { File.WriteAllText(result, "FAIL: " + ex); Environment.ExitCode = 1; }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void TestSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "ClipboardBridge-settings-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "settings.dat");
            try
            {
                string key = Convert.ToBase64String(Channel.RandomBytes(32));
                var original = new Settings { Role = 1, Port = 49000, Address = "192.168.1.42", Key = key, AutoStart = true, SyncFiles = false };
                original.Save(path); Settings saved = Settings.Load(path);
                Assert(saved.Key == key && saved.Role == 1 && saved.Port == 49000 && saved.Address == original.Address && saved.AutoStart && !saved.SyncFiles, "Protected settings roundtrip");
                Assert(Encoding.UTF8.GetString(File.ReadAllBytes(path)).IndexOf(key, StringComparison.Ordinal) < 0, "Secret encrypted at rest");
                original.AutoStart = false; original.Save(path); Assert(!Settings.Load(path).AutoStart, "Atomic settings replacement");
                Assert(Settings.StartupCommand(@"C:\App Folder\ClipboardBridge.exe") == "\"C:\\App Folder\\ClipboardBridge.exe\" --autostart", "Quoted startup command");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        static void TestProtocol()
        {
            byte[] key = Channel.RandomBytes(32);
            byte[] fileData = Channel.RandomBytes(524288);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Exception error = null;
            var thread = new Thread(delegate()
            {
                try
                {
                    using (var p = listener.AcceptTcpClient())
                    {
                        p.ReceiveTimeout = p.SendTimeout = 5000;
                        using (var c = new Channel(p.GetStream(), key, true))
                        {
                            byte[] b = c.Receive(); Assert(Encoding.UTF8.GetString(b, 1, b.Length - 1) == "Grüße 🌙", "Unicode"); c.Send((byte)'T', Encoding.UTF8.GetBytes("Antwort"));
                            b = c.Receive(); Assert(b[0] == 'B' && BitConverter.ToInt64(b, 1) == fileData.Length, "File begin");
                            byte[] first = c.Receive(), second = c.Receive();
                            Assert(first[0] == 'D' && second[0] == 'D', "File chunks");
                            byte[] combined = Channel.Join(Channel.Slice(first, 1, first.Length - 1), Channel.Slice(second, 1, second.Length - 1));
                            Assert(Channel.Equal(fileData, combined), "Binary transfer integrity");
                            Assert(c.Receive()[0] == 'E', "File end");
                            c.Send((byte)'T', Encoding.UTF8.GetBytes("Datei geprüft"));
                        }
                    }
                }
                catch (Exception ex) { error = ex; }
            }); thread.Start();
            using (var p = new TcpClient("127.0.0.1", port))
            {
                p.ReceiveTimeout = p.SendTimeout = 5000;
                using (var c = new Channel(p.GetStream(), key, false))
                {
                    c.Send((byte)'T', Encoding.UTF8.GetBytes("Grüße 🌙")); byte[] b = c.Receive(); Assert(Encoding.UTF8.GetString(b, 1, b.Length - 1) == "Antwort", "Bidirectional");
                    c.Send((byte)'B', BitConverter.GetBytes((long)fileData.Length));
                    c.Send((byte)'D', Channel.Slice(fileData, 0, 262144)); c.Send((byte)'D', Channel.Slice(fileData, 262144, 262144)); c.Send((byte)'E', new byte[0]);
                    b = c.Receive(); Assert(Encoding.UTF8.GetString(b, 1, b.Length - 1) == "Datei geprüft", "File acknowledgement");
                }
            }
            thread.Join(); listener.Stop(); if (error != null) throw error;
            // Build a deterministic peer transcript, then reuse its handshake to exercise frame validation.
            byte[] remote = Channel.RandomBytes(32);
            using (var stream = new TestStream(key, remote, false)) using (var c = new Channel(stream, key, false))
            {
                stream.QueueFrame((byte)'T', Encoding.UTF8.GetBytes("valid"), 0, false); Assert(c.Receive()[0] == 'T', "Valid frame");
                stream.QueueFrame((byte)'T', Encoding.UTF8.GetBytes("replay"), 0, false);
                bool rejected = false; try { c.Receive(); } catch (IOException) { rejected = true; } Assert(rejected, "Replay rejection");
            }
            using (var stream = new TestStream(key, remote, false)) using (var c = new Channel(stream, key, false))
            {
                stream.QueueFrame((byte)'T', Encoding.UTF8.GetBytes("tamper"), 0, true);
                bool rejected = false; try { c.Receive(); } catch (IOException) { rejected = true; } Assert(rejected, "Tamper rejection");
            }
            using (var stream = new TestStream(key, remote, true))
            {
                bool rejected = false; try { using (var c = new Channel(stream, key, false)) { } } catch (IOException) { rejected = true; } Assert(rejected, "Wrong key rejection");
            }
        }
        static void TestFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "ClipboardBridge-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Ordner", "Leer")); File.WriteAllText(Path.Combine(root, "Ordner", "Grüße.txt"), "Hallo 🌙");
            string archive = null; string receivedRoot = null;
            try
            {
                archive = Files.Pack(new string[] { Path.Combine(root, "Ordner") }); string[] paths = Files.Unpack(archive); receivedRoot = Path.GetDirectoryName(paths[0]);
                Assert(File.ReadAllText(Path.Combine(paths[0], "Grüße.txt")) == "Hallo 🌙", "File roundtrip"); Assert(Directory.Exists(Path.Combine(paths[0], "Leer")), "Empty directory");
                foreach (string bad in new string[] { "../escape", "/absolute", "C:/escape", "a/../../escape", "a:ads", "CON.txt", "a./x", "a\\..\\escape" })
                {
                    bool rejected = false; try { Files.SafePath(root, bad); } catch (IOException) { rejected = true; } Assert(rejected, "Unsafe path " + bad);
                }
                string malicious = Path.Combine(root, "malicious.zip");
                using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) zip.CreateEntry("../escape.txt");
                bool blocked = false; try { Files.Unpack(malicious); } catch (IOException) { blocked = true; } Assert(blocked, "Malicious archive rejection");
                string duplicate = Path.Combine(root, "duplicate.zip");
                using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Create)) { zip.CreateEntry("same.txt"); zip.CreateEntry("SAME.txt"); }
                blocked = false; try { Files.Unpack(duplicate); } catch (IOException) { blocked = true; } Assert(blocked, "Case-insensitive duplicate rejection");
                blocked = false; try { Files.Pack(new string[] { Path.Combine(root, "Ordner") }, delegate { throw new OperationCanceledException(); }); } catch (OperationCanceledException) { blocked = true; } Assert(blocked, "Packing cancellation");
            }
            finally { if (archive != null) File.Delete(archive); if (receivedRoot != null) Directory.Delete(receivedRoot, true); Directory.Delete(root, true); }
        }
        sealed class TestStream : Stream
        {
            readonly byte[] key, remote; readonly bool badProof; byte[] context; readonly MemoryStream incoming = new MemoryStream(); int writes;
            public TestStream(byte[] key, byte[] remote, bool badProof) { this.key = key; this.remote = remote; this.badProof = badProof; }
            void Enqueue(byte[] b) { long p = incoming.Position; incoming.Position = incoming.Length; incoming.Write(b, 0, b.Length); incoming.Position = p; }
            public override void Write(byte[] b, int o, int n)
            {
                if (writes++ == 0)
                {
                    byte[] local = Channel.Slice(b, o, n); context = Channel.Join(Encoding.ASCII.GetBytes("ClipboardBridge/1"), local, remote);
                    Enqueue(remote); Enqueue(badProof ? new byte[32] : Channel.Hash(key, Channel.Join(context, new byte[] { 1 })));
                }
            }
            public void QueueFrame(byte kind, byte[] payload, ulong counter, bool tamper)
            {
                byte[] iv = Channel.RandomBytes(16), plain = Channel.Join(new byte[] { kind }, payload), cipher;
                using (Aes aes = Aes.Create()) { aes.Key = Channel.Hash(key, Channel.Join(context, Encoding.ASCII.GetBytes("encryption"))); aes.IV = iv; using (var t = aes.CreateEncryptor()) cipher = t.TransformFinalBlock(plain, 0, plain.Length); }
                byte[] body = Channel.Join(BitConverter.GetBytes(counter), iv, cipher);
                byte[] tag = Channel.Hash(Channel.Hash(key, Channel.Join(context, Encoding.ASCII.GetBytes("authentication"))), Channel.Join(new byte[] { 1 }, body));
                if (tamper) body[body.Length - 1] ^= 1;
                Enqueue(Channel.Join(BitConverter.GetBytes(body.Length + tag.Length), body, tag));
            }
            public override int Read(byte[] b, int o, int n) { return incoming.Read(b, o, n); }
            public override bool CanRead { get { return true; } } public override bool CanWrite { get { return true; } } public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { } public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long n) { throw new NotSupportedException(); }
        }
    }
}
