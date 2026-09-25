using System.Net;
using System.Net.Sockets;
using Foundation;
using LoopDPI.Core;
using UIKit;
using UniformTypeIdentifiers;

namespace PkgSender.iOS;

public sealed class MainViewController : UIViewController
{
    const int ServerPort = 9898;

    sealed class LibItem
    {
        public string Path = "";
        public string Format = "pkg";
        public string FileName = "";
        public string Title = "";
        public string TitleId = "";
        public long Size;
        public string Platform = "";
        public PkgInfo? Pkg;
        public bool Queued = true;
        public string State = "";
    }

    readonly List<LibItem> _lib = new();
    RangeFileServer? _server;
    bool _busy;
    long _lastPullGot;

    UITextField? _ipField;
    UILabel? _connLabel;
    UILabel? _statusLabel;
    UIProgressView? _prog;
    UIButton? _sendBtn;
    UITableView? _table;
    UILabel? _libHead;

    string SavedIp
    {
        get => NSUserDefaults.StandardUserDefaults.StringForKey("psip") ?? "192.168.1.";
        set => NSUserDefaults.StandardUserDefaults.SetString(value, "psip");
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Title = "PKG Sender";
        View!.BackgroundColor = UIColor.SystemBackground;

        // ScrollView principal
        var scroll = new UIScrollView 
        { 
            TranslatesAutoresizingMaskIntoConstraints = false,
            ShowsVerticalScrollIndicator = true,
            ShowsHorizontalScrollIndicator = false,
        };
        View.AddSubview(scroll);

        // Stack principal - contenedor de todo
        var mainStack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 10,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.AddSubview(mainStack);

        // Constraints del scroll
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scroll.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
        });

        // Constraints del mainStack (centrado con márgenes)
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            mainStack.TopAnchor.ConstraintEqualTo(scroll.TopAnchor, 12),
            mainStack.BottomAnchor.ConstraintEqualTo(scroll.BottomAnchor, -12),
            mainStack.LeadingAnchor.ConstraintEqualTo(scroll.LeadingAnchor, 16),
            mainStack.TrailingAnchor.ConstraintEqualTo(scroll.TrailingAnchor, -16),
            mainStack.WidthAnchor.ConstraintEqualTo(scroll.WidthAnchor, -32),
        });

        // ============================================
        // CARD 1: HERO (Título + IP + Botones)
        // ============================================
        var heroCard = MakeCard();
        
        var titleLabel = MkLabel("PKG Sender", 20, true);
        titleLabel.TextAlignment = UITextAlignment.Center;
        heroCard.AddArrangedSubview(titleLabel);
        
        var subtitleLabel = MkLabel("Instala PS4/PS5 por LAN", 13, false, UIColor.SecondaryLabel);
        subtitleLabel.TextAlignment = UITextAlignment.Center;
        heroCard.AddArrangedSubview(subtitleLabel);

        // Separator
        var sep1 = new UIView { BackgroundColor = UIColor.Separator, TranslatesAutoresizingMaskIntoConstraints = false };
        heroCard.AddArrangedSubview(sep1);
        sep1.HeightAnchor.ConstraintEqualTo(0.5f).Active = true;

        // IP Field
        _ipField = new UITextField
        {
            Placeholder = "IP de consola (ej: 192.168.1.105)",
            Text = SavedIp,
            BorderStyle = UITextBorderStyle.RoundedRect,
            KeyboardType = UIKeyboardType.NumbersAndPunctuation,
            AutocorrectionType = UITextAutocorrectionType.No,
            TextAlignment = UITextAlignment.Center,
        };
        _ipField.HeightAnchor.ConstraintEqualTo(40).Active = true;
        heroCard.AddArrangedSubview(_ipField);

        // Botones: Test, Detect, Guide
        var btnStack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.FillEqually,
        };
        btnStack.AddArrangedSubview(MkBtn("Test", async () => await TestAsync(), filled: false));
        btnStack.AddArrangedSubview(MkBtn("Detect", async () => await DetectAsync(), filled: false));
        btnStack.AddArrangedSubview(MkBtn("Guide", ShowGuide, filled: false));
        heroCard.AddArrangedSubview(btnStack);

        // Connection status
        _connLabel = MkLabel("no probado", 12, true, UIColor.SecondaryLabel);
        _connLabel.TextAlignment = UITextAlignment.Center;
        heroCard.AddArrangedSubview(_connLabel);

        mainStack.AddArrangedSubview(heroCard);

        // ============================================
        // CARD 2: ELF (pkg-receiver.elf)
        // ============================================
        var elfCard = MakeCard();
        var elfTitle = MkLabel("📦 pkg-receiver.elf (PS5)", 14, true);
        elfTitle.TextAlignment = UITextAlignment.Center;
        elfCard.AddArrangedSubview(elfTitle);

        var elfBtnStack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.FillEqually,
        };
        elfBtnStack.AddArrangedSubview(MkBtn("Guardar", async () => await ExportElfAsync(false), filled: false));
        elfBtnStack.AddArrangedSubview(MkBtn("Compartir", async () => await ExportElfAsync(true), filled: false));
        elfCard.AddArrangedSubview(elfBtnStack);
        mainStack.AddArrangedSubview(elfCard);

        // ============================================
        // CARD 3: LIBRARY
        // ============================================
        var libCard = MakeCard();
        
        var libHeaderStack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.EqualSpacing,
            Alignment = UIStackViewAlignment.Center,
        };
        _libHead = MkLabel("📚 Librería (0)", 16, true);
        libHeaderStack.AddArrangedSubview(_libHead);
        libHeaderStack.AddArrangedSubview(MkBtn("➕ Agregar", PickFlow, filled: false));
        libCard.AddArrangedSubview(libHeaderStack);

        // Tabla
        _table = new UITableView 
        { 
            RowHeight = 60, 
            ScrollEnabled = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
            SeparatorStyle = UITableViewCellSeparatorStyle.SingleLine,
        };
        _table.HeightAnchor.ConstraintEqualTo(240).Active = true;
        _table.Source = new LibSource(this);
        libCard.AddArrangedSubview(_table);
        mainStack.AddArrangedSubview(libCard);

        // ============================================
        // CARD 4: ENVÍO Y PROGRESO
        // ============================================
        var sendCard = MakeCard();

        _sendBtn = MkBtn("📤 Enviar Cola", async () => await SendQueueAsync(), filled: true);
        _sendBtn.HeightAnchor.ConstraintEqualTo(48).Active = true;
        sendCard.AddArrangedSubview(_sendBtn);

        _prog = new UIProgressView(UIProgressViewStyle.Default);
        _prog.HeightAnchor.ConstraintEqualTo(6).Active = true;
        sendCard.AddArrangedSubview(_prog);

        _statusLabel = MkLabel("Agrega un PKG y presiona Enviar", 12, false, UIColor.SecondaryLabel);
        _statusLabel.TextAlignment = UITextAlignment.Center;
        _statusLabel.Lines = 3;
        _statusLabel.LineBreakMode = UILineBreakMode.WordWrap;
        sendCard.AddArrangedSubview(_statusLabel);

        mainStack.AddArrangedSubview(sendCard);

        // Botón About en la navbar
        NavigationItem.RightBarButtonItem = new UIBarButtonItem("ℹ️", UIBarButtonItemStyle.Plain,
            (_, _) => ShowAbout());
        
        RefreshLib();
    }

    // ============================================
    // HELPER FUNCTIONS
    // ============================================

    static UIStackView MakeCard()
    {
        var card = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 10,
            Alignment = UIStackViewAlignment.Fill,
            LayoutMarginsRelativeArrangement = true,
        };
        card.LayoutMargins = new UIEdgeInsets(14, 14, 14, 14);
        card.Layer.CornerRadius = 14;
        card.Layer.MasksToBounds = true;
        card.BackgroundColor = UIColor.SecondarySystemBackground;
        return card;
    }

    static UILabel MkLabel(string text, nfloat size, bool bold, UIColor? color = null)
    {
        var label = new UILabel
        {
            Text = text,
            Font = bold ? UIFont.BoldSystemFontOfSize(size) : UIFont.SystemFontOfSize(size),
            LineBreakMode = UILineBreakMode.TailTruncation,
        };
        if (color != null) label.TextColor = color;
        return label;
    }

    static UIButton MkBtn(string title, Action action, bool filled = false)
    {
        var btn = new UIButton(UIButtonType.System);
        btn.SetTitle(title, UIControlState.Normal);
        btn.TitleLabel!.Font = UIFont.SystemFontOfSize(14, UIFontWeight.Medium);
        btn.Layer.CornerRadius = 10;
        btn.Layer.MasksToBounds = true;

        if (filled)
        {
            btn.BackgroundColor = UIColor.SystemBlue;
            btn.SetTitleColor(UIColor.White, UIControlState.Normal);
        }
        else
        {
            btn.BackgroundColor = UIColor.TertiarySystemBackground;
            btn.SetTitleColor(UIColor.SystemBlue, UIControlState.Normal);
        }

        btn.ContentEdgeInsets = new UIEdgeInsets(10, 16, 10, 16);
        btn.TouchUpInside += (_, _) => action();
        return btn;
    }

    void Say(string s) => InvokeOnMainThread(() => 
    { 
        if (_statusLabel != null) _statusLabel.Text = s; 
    });

    void SetConn(bool? ok, string t) => InvokeOnMainThread(() =>
    {
        if (_connLabel == null) return;
        _connLabel.Text = t;
        _connLabel.TextColor = ok == true ? UIColor.SystemGreen 
            : ok == false ? UIColor.SystemRed 
            : UIColor.SecondaryLabel;
    });

    void RefreshLib() => InvokeOnMainThread(() =>
    {
        if (_libHead != null) _libHead.Text = $"📚 Librería ({_lib.Count})";
        if (_sendBtn != null) 
        { 
            int q = _lib.Count(x => x.Queued); 
            _sendBtn.SetTitle(q > 0 ? $"📤 Enviar ({q})" : "📤 Enviar Cola", UIControlState.Normal); 
        }
        _table?.ReloadData();
    });

    static string Short(string s) => s.Length > 140 ? s[..140] : s;
    static string SizeStr(long n) => n >= 1L << 30 ? $"{n / 1073741824.0:0.0} GB" : $"{n / 1048576.0:0.0} MB";

    // ============================================
    // FILE PICKING
    // ============================================

    void PickFlow()
    {
        var types = new[] { UTTypes.Data };
        var picker = new UIDocumentPickerViewController(types, true);
        picker.AllowsMultipleSelection = true;
        picker.DidPickDocument += async (_, e) =>
        {
            var urls = new[] { e.Url };
            Say($"leyendo {urls.Length} archivo(s)…");
            int n = 0;
            foreach (var url in urls) { if (await AddUrlAsync(url)) n++; }
            RefreshLib();
            Say(n > 0 ? $"✓ {n} agregado(s)" : "no agregado");
        };
        PresentViewController(picker, true, null);
    }

    async Task<bool> AddUrlAsync(NSUrl url)
    {
        try
        {
            bool access = url.StartAccessingSecurityScopedResource();
            try
            {
                string name = url.LastPathComponent ?? "game.pkg";
                string tmp = Path.Combine(Path.GetTempPath(), name);
                if (!File.Exists(tmp))
                {
                    using var src = File.OpenRead(url.Path!);
                    using var dst = File.Create(tmp);
                    await src.CopyToAsync(dst);
                }
                string low = name.ToLowerInvariant();
                string fmt = low.EndsWith(".exfat") ? "exfat" : low.EndsWith(".ffpfsc") ? "ffpfsc"
                    : low.EndsWith(".ffpkg") ? "ffpkg" : low.EndsWith(".pfs") ? "pfs" : "pkg";
                PkgInfo? pkg = null;
                try { pkg = GameReader.Read(tmp); }
                catch (Exception ex) { Say("error: " + Short(ex.Message)); return false; }
                lock (_lib)
                {
                    if (_lib.Any(x => x.Path == tmp)) return false;
                    _lib.Add(new LibItem
                    {
                        Path = tmp, Format = fmt, FileName = name,
                        Title = pkg?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(name),
                        TitleId = pkg?.TitleId is { Length: > 0 } i ? i : GameReader.TitleIdFromName(name),
                        Size = pkg != null && pkg.PackageSize > 0 ? pkg.PackageSize : new FileInfo(tmp).Length,
                        Platform = pkg?.Platform ?? "", Pkg = pkg, Queued = true,
                    });
                }
                return true;
            }
            finally { if (access) url.StopAccessingSecurityScopedResource(); }
        }
        catch (Exception ex) { Say("error: " + Short(ex.Message)); return false; }
    }

    // ============================================
    // ELF EXPORT
    // ============================================

    async Task ExportElfAsync(bool share)
    {
        try
        {
            Say("copiando ELF…");
            using var s = GetType().Assembly.GetManifestResourceStream("pkg-receiver.elf")
                ?? throw new IOException("ELF no encontrado");
            string tmp = Path.Combine(Path.GetTempPath(), "pkg-receiver.elf");
            using (var dst = File.Create(tmp)) await s.CopyToAsync(dst);
            if (!share) { Say("✓ Guardado en tmp"); return; }
            var vc = new UIActivityViewController(new NSObject[] { NSUrl.FromFilename(tmp) }, null);
            PresentViewController(vc, true, null);
        }
        catch (Exception ex) { Say("error: " + Short(ex.Message)); }
    }

    // ============================================
    // TEST / DETECT
    // ============================================

    string PsIp => (_ipField?.Text ?? "").Trim();

    async Task TestAsync()
    {
        string psIp = PsIp;
        if (string.IsNullOrEmpty(psIp)) { Say("escribe la IP primero"); return; }
        SavedIp = psIp;
        try
        {
            Say("probando…");
            SetConn(null, "probando…");
            string mode = await Ps4Installer.DetectAsync(psIp, fresh: true);
            string pcIp = await Task.Run(() => PhoneIpFor(psIp));
            if (mode == "offline")
            {
                SetConn(false, "✗ Desconectada");
                Say($"consola offline ({psIp})");
                return;
            }
            Say($"✓ Modo: {mode}");
            SetConn(true, $"✓ Conectada ({mode})");
        }
        catch (Exception ex) { SetConn(false, "✗ Error"); Say("error: " + Short(ex.Message)); }
    }

    async Task DetectAsync()
    {
        try
        {
            Say("escuchando señales…");
            SetConn(null, "detectando…");
            string? ip = await Task.Run(() => ListenForBeacon(TimeSpan.FromSeconds(6)));
            if (string.IsNullOrEmpty(ip))
            {
                SetConn(false, "✗ Sin señal");
                Say("no hay señal (consola apagada / otra red)");
                return;
            }
            InvokeOnMainThread(() => { if (_ipField != null) _ipField.Text = ip; });
            SavedIp = ip;
            Say($"✓ Encontrada en {ip}");
            await TestAsync();
        }
        catch (Exception ex) { Say("error: " + Short(ex.Message)); }
    }

    static string? ListenForBeacon(TimeSpan wait)
    {
        try
        {
            using var udp = new UdpClient(12801);
            udp.Client.ReceiveTimeout = 500;
            var deadline = DateTime.UtcNow + wait;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = udp.Receive(ref ep);
                    string msg = System.Text.Encoding.ASCII.GetString(buf);
                    if (!msg.StartsWith("PKGSENDER", StringComparison.Ordinal)) continue;
                    if (IPAddress.IsLoopback(ep.Address)) continue;
                    return ep.Address.ToString();
                }
                catch (SocketException) { }
            }
        }
        catch { }
        return null;
    }

    string PhoneIpFor(string psIp)
    {
        var nets = NetDiscovery.GetLanNetworks();
        try
        {
            var ps = IPAddress.Parse(psIp);
            string? same = nets.FirstOrDefault(n => n.Contains(ps))?.Address.ToString();
            if (same != null) return same;
        }
        catch { }
        return NetDiscovery.BestPcIpFor(nets, psIp) ?? "0.0.0.0";
    }

    // ============================================
    // SEND QUEUE
    // ============================================

    async Task SendQueueAsync()
    {
        if (_busy) return;
        string psIp = PsIp;
        if (string.IsNullOrEmpty(psIp)) { Say("escribe la IP primero"); return; }
        SavedIp = psIp;
        List<LibItem> queue;
        lock (_lib) queue = _lib.Where(x => x.Queued).ToList();
        if (queue.Count == 0) { Say("cola vacía"); return; }
        _busy = true;
        try
        {
            string pcIp = await Task.Run(() => PhoneIpFor(psIp));
            int done = 0;
            foreach (var it in queue)
            {
                it.State = "enviando…"; 
                RefreshLib();
                Say($"enviando: {it.Title}");
                bool ok = await SendOneAsync(psIp, pcIp, it);
                it.State = ok ? "✓ listo" : "✗ error";
                if (ok) done++;
                int d = done, n = queue.Count;
                InvokeOnMainThread(() => _prog?.SetProgress(d / (float)n, true));
                Say($"{d}/{n} enviados");
            }
            Say(done == queue.Count ? $"✓ todos enviados ({done})" : $"⚠ {done}/{queue.Count}");
        }
        catch (Exception ex) { Say("error: " + Short(ex.Message)); }
        finally
        {
            _busy = false;
            lock (_lib) foreach (var q in queue) if (q.State.StartsWith("✓")) q.Queued = false;
            RefreshLib();
        }
    }

    async Task<bool> SendOneAsync(string psIp, string pcIp, LibItem it)
    {
        try
        {
            _server?.Dispose();
            _server = new RangeFileServer(new Dictionary<string, string> { ["pkg"] = it.Path }, ServerPort);
            _server.Start();
            string url = _server.UrlFor(pcIp, "pkg");
            var pkg = it.Pkg;
            bool isPs4 = (pkg?.Platform ?? "").StartsWith("PS4");
            string? iconUrl = null;
            if (pkg?.IconData is { Length: > 0 })
            {
                string iconId = "icon" + DateTime.UtcNow.Ticks;
                _server.RegisterIcon(iconId, pkg.IconData);
                iconUrl = _server.IconUrlFor(pcIp, iconId);
            }
            if (it.Format != "pkg")
            {
                string remote = "/data/homebrew/" + it.FileName;
                for (int attempt = 1; attempt <= 6; attempt++)
                {
                    Say(attempt > 1 ? $"reintentando ({attempt}/6)…" : $"copiando…");
                    var (pok, preply) = await ConsoleClient.PullAsync(psIp, url, remote, resume: true);
                    if (!pok) return false;
                    if (await TrackPullAsync(psIp, remote, it)) return true;
                }
                return false;
            }
            var (ok, _) = await Ps4Installer.PushRpiAsync(psIp, url, it.Title, iconUrl);
            if (!ok && isPs4 && pkg != null)
            {
                _server.RegisterManifest("pkg", Ps4Installer.BuildManifest(url, it.Size, pkg.Digest));
                var g = await Ps4Installer.PushGoldHenAsync(psIp, pcIp, _server.ManifestUrlFor(pcIp, "pkg"), pkg, ServerPort);
                ok = g.Ok;
            }
            if (!ok) return false;
            return await TrackInstallAsync(_server, it);
        }
        catch { return false; }
    }

    async Task<bool> TrackInstallAsync(RangeFileServer? srv, LibItem it)
    {
        long t0 = Environment.TickCount64, last = 0, stall = t0;
        while (true)
        {
            await Task.Delay(1000);
            long served = srv?.ServedFor("pkg") ?? 0;
            long now = Environment.TickCount64;
            if (served > last) { last = served; stall = now; }
            long s = Math.Min(served, it.Size);
            double pct = it.Size > 0 ? 100.0 * s / it.Size : 0;
            InvokeOnMainThread(() => _prog?.SetProgress((float)(pct / 100), false));
            Say($"instalando… {pct:0}%");
            if (served >= it.Size && it.Size > 0) return true;
            if (now - stall > 120000) return served >= it.Size && it.Size > 0;
            if (now - t0 > 6 * 60 * 60 * 1000L) return false;
        }
    }

    async Task<bool> TrackPullAsync(string psIp, string remote, LibItem it)
    {
        long t0 = Environment.TickCount64;
        while (true)
        {
            await Task.Delay(1000);
            var (active, _, got, want, paused) = await ConsoleClient.GetPullAsync(psIp);
            _lastPullGot = got;
            if (!active) break;
            InvokeOnMainThread(() => _prog?.SetProgress(want > 0 ? got / (float)want : 0, false));
            Say($"copiando… {got / 1048576.0:0} MB");
            if (Environment.TickCount64 - t0 > 6 * 60 * 60 * 1000L) return false;
        }
        var (exists, size) = await ConsoleClient.StatAsync(psIp, remote);
        if (exists && size == it.Size) return true;
        return false;
    }

    // ============================================
    // ABOUT / GUIDE
    // ============================================

    void ShowAbout()
    {
        var a = UIAlertController.Create("PKG Sender",
            "Instala juegos PS4/PS5 por LAN\nby Loopayeh\n\ngithub.com/Loopayeh/pkg-sender",
            UIAlertControllerStyle.Alert);
        a.AddAction(UIAlertAction.Create("Cerrar", UIAlertActionStyle.Default, null));
        PresentViewController(a, true, null);
    }

    void ShowGuide()
    {
        var a = UIAlertController.Create("Guía de configuración",
            "1️⃣ Conecta la consola con cable LAN\n2️⃣ iPhone en la misma Wi-Fi\n3️⃣ Ejecuta pkg-receiver.elf (PS5)\n4️⃣ Escribe la IP y presiona Test\n5️⃣ Agrega juegos y Envía\n\n⚠️ Mantén la app abierta mientras se transfiere",
            UIAlertControllerStyle.Alert);
        a.AddAction(UIAlertAction.Create("Cerrar", UIAlertActionStyle.Default, null));
        PresentViewController(a, true, null);
    }

    // ============================================
    // TABLE VIEW
    // ============================================

    sealed class LibSource : UITableViewSource
    {
        readonly MainViewController _v;
        public LibSource(MainViewController v) => _v = v;
        
        public override nint RowsInSection(UITableView t, nint s) 
        { 
            lock (_v._lib) return _v._lib.Count; 
        }

        public override UITableViewCell GetCell(UITableView t, NSIndexPath p)
        {
            LibItem it;
            lock (_v._lib) it = _v._lib[p.Row];
            
            var c = t.DequeueReusableCell("lib") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "lib");
            c.TextLabel!.Text = $"{(it.Queued ? "☑️" : "☐")} {it.Title}";
            c.TextLabel.Font = UIFont.SystemFontOfSize(14, UIFontWeight.Medium);
            
            c.DetailTextLabel!.Text = $"{it.Format.ToUpper()} • {it.TitleId} • {_v.SizeStr(it.Size)} • {it.State}";
            c.DetailTextLabel.Font = UIFont.SystemFontOfSize(12);
            c.DetailTextLabel.TextColor = UIColor.SecondaryLabel;
            
            c.Accessory = UITableViewCellAccessory.DisclosureIndicator;
            return c;
        }

        public override void RowSelected(UITableView t, NSIndexPath p)
        {
            if (_v._busy) return;
            lock (_v._lib) _v._lib[p.Row].Queued = !_v._lib[p.Row].Queued;
            _v.RefreshLib();
            t.DeselectRow(p, true);
        }
    }

    // Helper method - debe ser public para la tabla
    public static string SizeStr(long n) => n >= 1L << 30 ? $"{n / 1073741824.0:0.0} GB" : $"{n / 1048576.0:0.0} MB";
}
