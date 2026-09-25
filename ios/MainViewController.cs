using System.Net;
using System.Net.Sockets;
using Foundation;
using LoopDPI.Core;
using UIKit;

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
        public NSUrl? SecurityScopedUrl; // Guardado para mantener permisos de lectura activa
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

        // Configuración del ScrollView
        var scroll = new UIScrollView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            AlwaysBounceVertical = true,
            AlwaysBounceHorizontal = true,
            ShowsHorizontalScrollIndicator = true,
            ShowsVerticalScrollIndicator = true,
        };
        View.AddSubview(scroll);

        var stack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 12,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.AddSubview(stack);

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scroll.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor),
        });

        NSLayoutConstraint.ActivateConstraints(new[]
        {
            stack.TopAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TopAnchor, 12),
            stack.BottomAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.BottomAnchor, -12),
            stack.LeadingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.LeadingAnchor, 16),
            stack.TrailingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TrailingAnchor, -16),
            stack.WidthAnchor.ConstraintGreaterThanOrEqualTo(scroll.FrameLayoutGuide.WidthAnchor, -32),
        });

        // CARD 1: HERO
        var hero = Card();
        hero.AddArrangedSubview(MkLabel("PKG Sender", 20, true));
        hero.AddArrangedSubview(MkLabel("PS4 / PS5 over LAN", 13, false, UIColor.SecondaryLabel));
        
        _ipField = new UITextField
        {
            Placeholder = "Console IP (192.168.1.105)",
            Text = SavedIp,
            BorderStyle = UITextBorderStyle.RoundedRect,
            KeyboardType = UIKeyboardType.NumbersAndPunctuation,
            AutocorrectionType = UITextAutocorrectionType.No,
        };
        _ipField.HeightAnchor.ConstraintEqualTo(40).Active = true;
        hero.AddArrangedSubview(_ipField);

        var btnRow = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.FillEqually,
        };
        btnRow.AddArrangedSubview(MkBtn("Test", async () => await TestAsync(), filled: false));
        btnRow.AddArrangedSubview(MkBtn("Detect", async () => await DetectAsync(), filled: false));
        btnRow.AddArrangedSubview(MkBtn("Guide", ShowGuide, filled: false));
        hero.AddArrangedSubview(btnRow);

        _connLabel = MkLabel("not tested", 12, true, UIColor.SecondaryLabel);
        hero.AddArrangedSubview(_connLabel);
        stack.AddArrangedSubview(hero);

        // CARD 2: ELF
        var elf = Card();
        elf.AddArrangedSubview(MkLabel("pkg-receiver.elf (PS5 only)", 14, true));
        
        var elfRow = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.FillEqually,
        };
        elfRow.AddArrangedSubview(MkBtn("Save", async () => await ExportElfAsync(false), filled: false));
        elfRow.AddArrangedSubview(MkBtn("Share", async () => await ExportElfAsync(true), filled: false));
        elf.AddArrangedSubview(elfRow);
        stack.AddArrangedSubview(elf);

        // CARD 3: LIBRARY
        var lib = Card();
        
        var libHeader = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Spacing = 8,
            Distribution = UIStackViewDistribution.EqualSpacing,
            Alignment = UIStackViewAlignment.Center,
        };
        _libHead = MkLabel("Library (0)", 16, true);
        libHeader.AddArrangedSubview(_libHead);
        libHeader.AddArrangedSubview(MkBtn("+ Add", PickFlow, filled: false));
        lib.AddArrangedSubview(libHeader);

        _table = new UITableView
        {
            RowHeight = 60,
            ScrollEnabled = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        _table.HeightAnchor.ConstraintEqualTo(240).Active = true;
        _table.Source = new LibSource(this);
        lib.AddArrangedSubview(_table);
        stack.AddArrangedSubview(lib);

        // CARD 4: SEND
        var send = Card();
        
        _sendBtn = MkBtn("Send queue", async () => await SendQueueAsync(), filled: true);
        _sendBtn.HeightAnchor.ConstraintEqualTo(44).Active = true;
        send.AddArrangedSubview(_sendBtn);

        _prog = new UIProgressView(UIProgressViewStyle.Default);
        send.AddArrangedSubview(_prog);

        _statusLabel = MkLabel("add a PKG, tick it, then Send", 11, false, UIColor.SecondaryLabel);
        _statusLabel.Lines = 3;
        send.AddArrangedSubview(_statusLabel);
        
        stack.AddArrangedSubview(send);

        NavigationItem.RightBarButtonItem = new UIBarButtonItem("About", UIBarButtonItemStyle.Plain,
            (_, _) => ShowAbout());
        
        RefreshLib();
    }

    static UIStackView Card()
    {
        var card = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 10,
            Alignment = UIStackViewAlignment.Fill,
            LayoutMarginsRelativeArrangement = true,
        };
        card.LayoutMargins = new UIEdgeInsets(12, 12, 12, 12);
        card.Layer.CornerRadius = 12;
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
            Lines = 0,
        };
        if (color != null) label.TextColor = color;
        return label;
    }

    static UIButton MkBtn(string title, Action action, bool filled = false)
    {
        var btn = new UIButton(UIButtonType.System);
        btn.SetTitle(title, UIControlState.Normal);
        btn.TitleLabel!.Font = UIFont.SystemFontOfSize(13, UIFontWeight.Medium);
        btn.Layer.CornerRadius = 8;
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

        btn.TouchUpInside += (_, _) => action();
        return btn;
    }

    void Say(string s) => InvokeOnMainThread(() => { if (_statusLabel != null) _statusLabel.Text = s; });

    void SetConn(bool? ok, string t) => InvokeOnMainThread(() =>
    {
        if (_connLabel == null) return;
        _connLabel.Text = t;
        _connLabel.TextColor = ok == true ? UIColor.SystemGreen : ok == false ? UIColor.SystemRed : UIColor.SecondaryLabel;
    });

    void RefreshLib() => InvokeOnMainThread(() =>
    {
        if (_libHead != null) _libHead.Text = $"Library ({_lib.Count})";
        if (_sendBtn != null)
        {
            int q = _lib.Count(x => x.Queued);
            _sendBtn.SetTitle(q > 0 ? $"Send queue ({q})" : "Send queue", UIControlState.Normal);
        }
        _table?.ReloadData();
    });

    static string Short(string s) => s.Length > 140 ? s[..140] : s;
    static string SizeStr(long n) => n >= 1L << 30 ? $"{n / 1073741824.0:0.0} GB" : $"{n / 1048576.0:0.0} MB";

    void PickFlow()
    {
        string[] allowedTypes = new string[] 
        { 
            "public.item", 
            "public.data", 
            "public.content" 
        };

        var picker = new UIDocumentPickerViewController(allowedTypes, UIDocumentPickerMode.Open)
        {
            AllowsMultipleSelection = true,
            ShouldOpenInPlace = true // Lee el archivo original in-situ sin duplicarlo en almacenamiento local
        };

        picker.DidPickDocumentAtUrls += (sender, e) =>
        {
            // 1. Cierre inmediato del selector de archivos
            picker.DismissViewController(true, async () =>
            {
                if (e.Urls == null || e.Urls.Length == 0) return;

                Say($"Agregando {e.Urls.Length} archivo(s)…");
                int n = 0;

                foreach (var url in e.Urls)
                {
                    if (await AddUrlAsync(url)) n++;
                }
                RefreshLib();
                Say(n > 0 ? $"{n} agregado(s) — listo para enviar" : "No se pudo agregar el archivo");
            });
        };

        picker.WasCancelled += (_, _) =>
        {
            picker.DismissViewController(true, null);
        };

        PresentViewController(picker, true, null);
    }

    async Task<bool> AddUrlAsync(NSUrl url)
    {
        bool access = false;
        try
        {
            // Solicitar permisos de acceso directo en la Sandbox de iOS
            access = url.StartAccessingSecurityScopedResource();
            
            string filePath = url.Path ?? "";
            string name = url.LastPathComponent ?? "game.pkg";

            if (!File.Exists(filePath))
            {
                Say($"Ruta no accesible: {name}");
                if (access) url.StopAccessingSecurityScopedResource();
                return false;
            }

            string low = name.ToLowerInvariant();
            string fmt = low.EndsWith(".exfat") ? "exfat" : low.EndsWith(".ffpfsc") ? "ffpfsc"
                : low.EndsWith(".ffpkg") ? "ffpkg" : low.EndsWith(".pfs") ? "pfs" : "pkg";

            PkgInfo? pkg = null;

            // Procesamiento en segundo plano de metadatos (lectura streaming rápida del header)
            await Task.Run(() =>
            {
                try 
                { 
                    pkg = GameReader.Read(filePath);
                }
                catch (Exception ex) 
                { 
                    Say("Warning metadatos: " + Short(ex.Message)); 
                }
            });

            lock (_lib)
            {
                if (_lib.Any(x => x.Path == filePath))
                {
                    if (access) url.StopAccessingSecurityScopedResource();
                    return false;
                }

                _lib.Add(new LibItem
                {
                    Path = filePath,
                    Format = fmt,
                    FileName = name,
                    Title = pkg?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(name),
                    TitleId = pkg?.TitleId is { Length: > 0 } i ? i : GameReader.TitleIdFromName(name),
                    Size = pkg != null && pkg.PackageSize > 0 ? pkg.PackageSize : new FileInfo(filePath).Length,
                    Platform = pkg?.Platform ?? "",
                    Pkg = pkg,
                    Queued = true,
                    SecurityScopedUrl = access ? url : null // Retener permisos para cuando se inicie la transmisión
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            Say("Error al agregar: " + Short(ex.Message));
            if (access) url.StopAccessingSecurityScopedResource();
            return false;
        }
    }

    async Task ExportElfAsync(bool share)
    {
        try
        {
            Say("copying ELF…");
            using var s = GetType().Assembly.GetManifestResourceStream("pkg-receiver.elf")
                ?? throw new IOException("bundled ELF missing");
            string tmp = Path.Combine(Path.GetTempPath(), "pkg-receiver.elf");
            using (var dst = File.Create(tmp)) await s.CopyToAsync(dst);
            if (!share) { Say("saved to tmp/pkg-receiver.elf — copy it via Files app"); return; }
            var vc = new UIActivityViewController(new NSObject[] { NSUrl.FromFilename(tmp) }, null);
            PresentViewController(vc, true, null);
            Say("share sheet opened");
        }
        catch (Exception ex) { Say("ELF failed: " + Short(ex.Message)); }
    }

    string PsIp => (_ipField?.Text ?? "").Trim();

    async Task TestAsync()
    {
        string psIp = PsIp;
        if (string.IsNullOrEmpty(psIp)) { Say("type the console IP first"); return; }
        SavedIp = psIp;
        try
        {
            Say("probing console (12800/9090)…");
            SetConn(null, "probing…");
            string mode = await Ps4Installer.DetectAsync(psIp, fresh: true);
            string pcIp = await Task.Run(() => PhoneIpFor(psIp));
            if (mode == "offline")
            {
                string diag = await Ps4Installer.DiagnoseAsync(psIp);
                SetConn(false, "offline — " + psIp);
                Say($"OFFLINE {psIp} phone={pcIp}\n{diag}");
                return;
            }
            Say($"console={mode} phone={pcIp}");
            SetConn(true, $"connected ({mode}) • {psIp}");
        }
        catch (Exception ex) { SetConn(false, "test failed"); Say("test error: " + Short(ex.Message)); }
    }

    async Task DetectAsync()
    {
        try
        {
            Say("listening for console beacons…");
            SetConn(null, "detecting…");
            string? ip = await Task.Run(() => ListenForBeacon(TimeSpan.FromSeconds(6)));
            if (string.IsNullOrEmpty(ip))
            {
                SetConn(false, "no beacon — type IP manually");
                Say("no beacon heard: console off / other network / AP isolation.");
                return;
            }
            InvokeOnMainThread(() => { if (_ipField != null) _ipField.Text = ip; });
            SavedIp = ip;
            Say($"found console at {ip} — testing…");
            await TestAsync();
        }
        catch (Exception ex) { Say("detect error: " + Short(ex.Message)); }
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

    async Task SendQueueAsync()
    {
        if (_busy) return;
        string psIp = PsIp;
        if (string.IsNullOrEmpty(psIp)) { Say("type the console IP first"); return; }
        SavedIp = psIp;
        List<LibItem> queue;
        lock (_lib) queue = _lib.Where(x => x.Queued).ToList();
        if (queue.Count == 0) { Say("queue is empty — tick some games"); return; }
        _busy = true;
        try
        {
            string pcIp = await Task.Run(() => PhoneIpFor(psIp));
            int done = 0;
            foreach (var it in queue)
            {
                it.State = "sending…"; RefreshLib();
                Say($"sending {it.Title}…");
                bool ok = await SendOneAsync(psIp, pcIp, it);
                it.State = ok ? "done" : "failed";
                if (ok) done++;
                int d = done, n = queue.Count;
                InvokeOnMainThread(() => _prog?.SetProgress(d / (float)n, true));
                Say($"{d}/{n} sent");
            }
            Say(done == queue.Count ? $"all {done} sent — watch the console." : $"{done}/{queue.Count} sent");
        }
        catch (Exception ex) { Say("error: " + Short(ex.Message)); }
        finally
        {
            _busy = false;
            lock (_lib)
            {
                foreach (var q in queue)
                {
                    if (q.State.StartsWith("done")) q.Queued = false;
                    
                    // Liberar recursos de seguridad al terminar
                    if (q.SecurityScopedUrl != null)
                    {
                        q.SecurityScopedUrl.StopAccessingSecurityScopedResource();
                        q.SecurityScopedUrl = null;
                    }
                }
            }
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
                    Say(attempt > 1 ? $"retrying copy ({attempt}/6)…" : $"copying {it.FileName}…");
                    var (pok, preply) = await ConsoleClient.PullAsync(psIp, url, remote, resume: true);
                    if (!pok) { Say($"copy failed: {Short(preply)}"); return false; }
                    if (await TrackPullAsync(psIp, remote, it)) return true;
                }
                Say("copy stalled after 6 tries");
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
            Say($"installing… {s / 1048576.0:0}/{it.Size / 1048576.0:0} MB ({pct:0}%) — keep the app open");
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
            Say($"copying… {got / 1048576.0:0}/{want / 1048576.0:0} MB{(paused ? " — paused" : "")}");
            if (Environment.TickCount64 - t0 > 6 * 60 * 60 * 1000L) return false;
        }
        var (exists, size) = await ConsoleClient.StatAsync(psIp, remote);
        if (exists && size == it.Size) return true;
        Say($"landed size mismatch (console {size}, want {it.Size})");
        return false;
    }

    void ShowAbout()
    {
        var a = UIAlertController.Create("PKG Sender (iOS) • by Loopayeh",
            "Installs PS4/PS5 games over LAN.\nRun pkg-receiver.elf on PS5, or Remote Package Installer / GoldHEN on PS4.\n\ngithub.com/Loopayeh/pkg-sender",
            UIAlertControllerStyle.Alert);
        a.AddAction(UIAlertAction.Create("Close", UIAlertActionStyle.Default, null));
        PresentViewController(a, true, null);
    }

    void ShowGuide()
    {
        var a = UIAlertController.Create("Setup guide • راهنما",
            "1) Console with LAN cable to the modem.\n2) iPhone to the same modem Wi-Fi (5GHz).\n3) PS5: run exploit + pkg-receiver.elf. PS4: open Remote Package Installer.\n4) Type console IP, Test (green = connected).\n5) + Add games, Send queue. Keep the app open mid-transfer.\n\n۱) کنسول با کابل لن به مودم. ۲) آیفون به وای‌فای همان مودم. ۳) Test سبز = وصله. ۴) وسط انتقال از برنامه بیرون نرو.",
            UIAlertControllerStyle.Alert);
        a.AddAction(UIAlertAction.Create("Close", UIAlertActionStyle.Default, null));
        PresentViewController(a, true, null);
    }

    sealed class LibSource : UITableViewSource
    {
        readonly MainViewController _v;
        public LibSource(MainViewController v) => _v = v;
        public override nint RowsInSection(UITableView t, nint s) { lock (_v._lib) return _v._lib.Count; }
        public override UITableViewCell GetCell(UITableView t, NSIndexPath p)
        {
            LibItem it;
            lock (_v._lib) it = _v._lib[p.Row];
            var c = t.DequeueReusableCell("lib") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "lib");
            
            c.TextLabel!.Text = $"{(it.Queued ? "☑ " : "☐ ")}{it.Title}";
            c.TextLabel.Font = UIFont.SystemFontOfSize(14);
            
            c.DetailTextLabel!.Text = $"{it.Format.ToUpperInvariant()} • {it.TitleId} • {SizeStr(it.Size)} • {it.State}";
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
}
