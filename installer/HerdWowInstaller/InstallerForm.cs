using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace HerdWowInstaller;

public class InstallerForm : Form
{
    // ── Win32 for frameless drag ──────────────────────────────────────────────
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern int  SendMessage(IntPtr h, int msg, int wp, int lp);
    const int WM_NCLBUTTONDOWN = 0xA1, HT_CAPTION = 2;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BG        = Color.FromArgb(14,  14,  24);
    static readonly Color CARD      = Color.FromArgb(18,  21,  30);
    static readonly Color BORDER    = Color.FromArgb(30,  33,  48);
    static readonly Color ACCENT    = Color.FromArgb(0,  150, 255);
    static readonly Color ACCENT2   = Color.FromArgb(0,  100, 210);
    static readonly Color TXT       = Color.FromArgb(220, 225, 240);
    static readonly Color TXT2      = Color.FromArgb(107, 122, 153);
    static readonly Color INPUT_BG  = Color.FromArgb(8,   10,  16);
    static readonly Color BTN_DIM   = Color.FromArgb(28,  32,  48);

    // ── Download resilience ──────────────────────────────────────────────────
    const int DownloadMaxAttempts = 6;

    // ── Pages ─────────────────────────────────────────────────────────────────
    private readonly Panel _pageWelcome;
    private readonly Panel _pageLocate;
    private readonly Panel _pageInstall;
    private readonly Panel _pageFinish;
    private Panel _current;

    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly TextBox      _pathBox;
    private readonly Label        _installStatus;
    private readonly GradientBar  _progressBar;
    private readonly Label        _progressLabel;
    private readonly ListBox      _log;
    private readonly CheckBox     _launchCheck;
    private readonly Button       _nextBtn;
    private readonly Button       _backBtn;
    private readonly Button       _cancelBtn;

    private string _installPath = Config.DefaultInstallPath;
    private CancellationTokenSource? _cts;

    public InstallerForm()
    {
        Text            = Config.ServerName + " Setup";
        Size            = new Size(560, 440);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BG;
        Font            = new Font("Segoe UI", 9.5f);

        // Drop shadow
        var cp = base.CreateParams;
        cp.ClassStyle |= 0x20000;

        // Drag
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } };

        // ── Window chrome ─────────────────────────────────────────────────────
        var close = ChromeBtn(Width - 44, 0, true);
        var min   = ChromeBtn(Width - 88, 0, false);
        Controls.Add(close); Controls.Add(min);
        close.BringToFront(); min.BringToFront();

        // ── Nav bar ───────────────────────────────────────────────────────────
        var nav = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = CARD };
        nav.Controls.Add(DivLine(DockStyle.Top));

        _cancelBtn = NavBtn("Cancel", 20);
        _cancelBtn.Click += (s, e) => CancelClicked();
        nav.Controls.Add(_cancelBtn);

        _backBtn = NavBtn("← Back", 220);
        _backBtn.Enabled = false;
        _backBtn.Click += (s, e) => GoBack();
        nav.Controls.Add(_backBtn);

        _nextBtn = NavBtn("Next →", 310);
        _nextBtn.BackColor = ACCENT;
        _nextBtn.ForeColor = Color.White;
        _nextBtn.FlatAppearance.BorderColor = ACCENT2;
        _nextBtn.Click += (s, e) => GoNext();
        nav.Controls.Add(_nextBtn);

        Controls.Add(nav);

        // ── Welcome ───────────────────────────────────────────────────────────
        _pageWelcome = MakePage();
        PageHeader(_pageWelcome, "Welcome to Herd WoW", "Your WoW 3.3.5 private server experience.");

        AddLabel(_pageWelcome,
            "This wizard will:\n\n" +
            "  •  Download the Herd WoW client\n" +
            "  •  Extract it to a folder of your choice\n" +
            "  •  Install the Herd WoW Launcher\n" +
            "  •  Create a desktop shortcut\n\n" +
            "Make sure you have at least 25 GB of free disk space.\n\n" +
            "Click Next to choose where to install.",
            28, 148, 500, 210);

        // ── Choose location ───────────────────────────────────────────────────
        _pageLocate = MakePage();
        PageHeader(_pageLocate, "Choose Install Location", "Where should Herd WoW be installed?");

        AddLabel(_pageLocate, "Install folder:", 28, 148, 200, 20, bold: true);

        _pathBox = new TextBox
        {
            Location    = new Point(28, 172),
            Size        = new Size(400, 28),
            Text        = Config.DefaultInstallPath,
            BackColor   = INPUT_BG,
            ForeColor   = TXT,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = new Font("Segoe UI", 10f)
        };
        _pathBox.TextChanged += (s, e) => _installPath = _pathBox.Text.Trim();
        _pageLocate.Controls.Add(_pathBox);

        var browseBtn = DarkBtn("Browse...", 436, 171, 96, 28);
        browseBtn.Click += BrowseClicked;
        _pageLocate.Controls.Add(browseBtn);

        AddLabel(_pageLocate, "Example:  C:\\Herd WoW   or   D:\\Games\\Herd WoW",
            28, 208, 500, 18, italic: true, color: TXT2);
        AddLabel(_pageLocate, "⚠  Required disk space: approximately 25 GB",
            28, 240, 500, 20, color: Color.FromArgb(220, 160, 60));

        // ── Installing ────────────────────────────────────────────────────────
        _pageInstall = MakePage();
        PageHeader(_pageInstall, "Installing Herd WoW", "Please wait...");

        _installStatus = new Label
        {
            Text      = "Starting...",
            Location  = new Point(28, 110),
            Size      = new Size(500, 24),
            ForeColor = ACCENT,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        _pageInstall.Controls.Add(_installStatus);

        _progressBar = new GradientBar
        {
            Location = new Point(28, 142),
            Size     = new Size(500, 8)
        };
        _pageInstall.Controls.Add(_progressBar);

        _progressLabel = new Label
        {
            Location  = new Point(28, 156),
            Size      = new Size(500, 18),
            ForeColor = TXT2,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8.5f)
        };
        _pageInstall.Controls.Add(_progressLabel);

        _log = new ListBox
        {
            Location    = new Point(28, 182),
            Size        = new Size(500, 188),
            BackColor   = INPUT_BG,
            ForeColor   = Color.FromArgb(160, 180, 210),
            BorderStyle = BorderStyle.FixedSingle,
            Font        = new Font("Consolas", 8f)
        };
        _pageInstall.Controls.Add(_log);

        // ── Finish ────────────────────────────────────────────────────────────
        _pageFinish = MakePage();
        PageHeader(_pageFinish, "Installation Complete!", "Herd WoW is ready to play.");

        AddLabel(_pageFinish, "✓  Client installed successfully.", 28, 138, 500, 24,
            bold: true, color: Color.FromArgb(80, 210, 100));
        AddLabel(_pageFinish,
            "The Herd WoW Launcher has been placed in your install folder\n" +
            "and a shortcut has been added to your desktop.\n\n" +
            "Open the launcher, log in with your account, and click Play.\n" +
            "It will automatically download any server patches before launch.",
            28, 172, 500, 100);

        _launchCheck = new CheckBox
        {
            Text      = "Launch Herd WoW now",
            Checked   = true,
            Location  = new Point(28, 285),
            AutoSize  = true,
            ForeColor = TXT,
            Font      = new Font("Segoe UI", 10f)
        };
        _pageFinish.Controls.Add(_launchCheck);

        // ── Add pages ─────────────────────────────────────────────────────────
        foreach (var p in new[] { _pageWelcome, _pageLocate, _pageInstall, _pageFinish })
            Controls.Add(p);

        _current = _pageWelcome;
        ShowPage(_pageWelcome);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void ShowPage(Panel page)
    {
        foreach (var p in new[] { _pageWelcome, _pageLocate, _pageInstall, _pageFinish })
            p.Visible = false;
        page.Visible = true;
        _current = page;

        _backBtn.Enabled   = page == _pageLocate;
        _cancelBtn.Enabled = page != _pageFinish;
        _nextBtn.Enabled   = page != _pageInstall;
        _nextBtn.Text      = page == _pageFinish  ? "Finish"  :
                             page == _pageLocate  ? "Install" : "Next →";
        _nextBtn.BackColor = page == _pageFinish  ? Color.FromArgb(40, 160, 80) : ACCENT;
    }

    private void GoNext()
    {
        if (_current == _pageWelcome) { ShowPage(_pageLocate); return; }
        if (_current == _pageLocate)  { ValidateAndInstall(); return; }
        if (_current == _pageFinish)  { Finish(); }
    }

    private void GoBack() { if (_current == _pageLocate) ShowPage(_pageWelcome); }

    private void CancelClicked()
    {
        if (_current == _pageInstall)
        {
            if (MessageBox.Show("Cancel the installation?", "Cancel",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { _cts?.Cancel(); Application.Exit(); }
        }
        else Application.Exit();
    }

    private void Finish()
    {
        if (_launchCheck.Checked)
        {
            var exe = Path.Combine(_installPath, Config.LauncherExeName);
            if (File.Exists(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = exe, WorkingDirectory = _installPath, UseShellExecute = true });
        }
        Application.Exit();
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    private void BrowseClicked(object? sender, EventArgs e)
    {
        var chosen = BrowseForFolder(_installPath);
        if (chosen != null) { _installPath = chosen; _pathBox.Text = chosen; }
    }

    private static string? BrowseForFolder(string initial)
    {
        string? result = null;
        var t = new Thread(() =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description         = "Choose where to install Herd WoW",
                SelectedPath        = Directory.Exists(initial) ? initial : @"C:\",
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == DialogResult.OK) result = dlg.SelectedPath;
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return result;
    }

    // ── Validate + start install ──────────────────────────────────────────────

    private void ValidateAndInstall()
    {
        if (string.IsNullOrWhiteSpace(_installPath))
        { MessageBox.Show("Please enter an install folder.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        try { Path.GetFullPath(_installPath); }
        catch { MessageBox.Show("That path isn't valid.", "Invalid path", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        if (File.Exists(Path.Combine(_installPath, "Wow.exe")) || File.Exists(Path.Combine(_installPath, "WoW.exe")))
        {
            if (MessageBox.Show($"A WoW client already exists in:\n{_installPath}\n\nOverwrite it?",
                    "Folder not empty", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        ShowPage(_pageInstall);
        _cts = new CancellationTokenSource();
        _ = RunInstallAsync(_cts.Token);
    }

    // ── Install ───────────────────────────────────────────────────────────────

    private async Task RunInstallAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_installPath);

            // Step 1: Fetch the part list, then download + reassemble the client archive.
            // The base client is ~24GB, too big for one GitHub release asset, so it ships
            // as numbered parts that get concatenated back into one zip.
            SetStatus("Fetching client file list...");
            Log("Fetching client manifest...");
            var clientManifest = await FetchClientManifestAsync(ct);
            var parts = clientManifest.GetParts();
            Log($"Client archive: {parts.Count} parts.");

            var partsDir = Path.Combine(_installPath, "_herdwow_client_parts");
            Directory.CreateDirectory(partsDir);
            var partPaths = new List<string>();
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var partName = part.File;
                    var partUrl  = $"{Config.ReleaseBaseUrl}/{partName}";
                    var partPath = Path.Combine(partsDir, partName);
                    partPaths.Add(partPath);

                    // Skip if already downloaded and hash matches (allows resuming after failure/cancel)
                    if (File.Exists(partPath))
                    {
                        if (!string.IsNullOrEmpty(part.Sha256))
                        {
                            SetStatus($"Verifying part {i + 1}/{parts.Count}...");
                            var hash = await ComputeSha256Async(partPath);
                            if (hash.Equals(part.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"Part {i + 1}/{parts.Count} already downloaded and verified, skipping.");
                                continue;
                            }
                            else
                            {
                                Log($"Part {i + 1}/{parts.Count} hash mismatch, re-downloading...");
                                File.Delete(partPath);
                            }
                        }
                        else
                        {
                            Log($"Part {i + 1}/{parts.Count} already downloaded (no hash to verify), skipping.");
                            continue;
                        }
                    }

                    int partIndex = i; // captured for the progress closure
                    SetStatus($"Downloading client ({partIndex + 1}/{parts.Count})...");
                    await DownloadFileWithRetryAsync(partUrl, partPath, ct,
                        (pct, detail) => SetProgress((int)((partIndex + pct / 100.0) / parts.Count * 50),
                            $"Part {partIndex + 1}/{parts.Count}: {detail}"),
                        Log, "HerdWoW-Installer/1.0");

                    // Verify hash after download
                    if (!string.IsNullOrEmpty(part.Sha256))
                    {
                        SetStatus($"Verifying part {partIndex + 1}/{parts.Count}...");
                        var hash = await ComputeSha256Async(partPath);
                        if (!hash.Equals(part.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(partPath);
                            throw new Exception($"Part {partName} failed verification (SHA256 mismatch). Please try again.");
                        }
                        Log($"Part {partIndex + 1}/{parts.Count} verified.");
                    }
                }
                Log("All parts downloaded and verified.");
            }
            catch
            {
                // Clean up partial downloads on error
                Log("Download failed, cleaning up partial files...");
                foreach (var p in partPaths)
                {
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                    try { if (File.Exists(p + ".download")) File.Delete(p + ".download"); } catch { }
                }
                try { Directory.Delete(partsDir); } catch { }
                throw;
            }

            var archivePath = Path.Combine(_installPath, clientManifest.ArchiveName);
            SetStatus("Assembling client archive...");
            Log("Assembling downloaded parts...");
            SetProgress(50, "Assembling...");
            await AssembleFilesAsync(archivePath, partPaths);
            foreach (var p in partPaths) { try { File.Delete(p); } catch { } }
            try { Directory.Delete(partsDir); } catch { }
            Log("Client archive assembled.");

            // Step 2: Extract
            SetStatus("Extracting client...");
            Log($"Extracting to {_installPath}...");
            SetProgress(55, "Extracting files...");
            await Task.Run(() => Extract(archivePath, _installPath), ct);
            try { File.Delete(archivePath); } catch { }
            Log("Extraction complete.");

            // Step 3: Find WoW root (zip may have a subfolder)
            var wowRoot = FindWowRoot(_installPath) ?? _installPath;

            // Step 4: Download launcher
            SetStatus("Downloading launcher...");
            Log("Downloading Herd WoW Launcher...");
            var launcherPath = Path.Combine(wowRoot, Config.LauncherExeName);
            await DownloadFileWithRetryAsync(Config.LauncherDownloadUrl, launcherPath, ct,
                (pct, detail) => SetProgress(75 + pct / 5, $"Launcher: {detail}"), Log, "HerdWoW-Installer/1.0");
            Log("Launcher installed.");

            // Step 5: Shortcut
            SetProgress(97, "Creating desktop shortcut...");
            SetStatus("Creating shortcut...");
            try { CreateShortcut(launcherPath, wowRoot); Log("Desktop shortcut created."); }
            catch (Exception ex) { Log($"Warning: {ex.Message}"); }

            SetProgress(100, "Done!");
            SetStatus("Installation complete!");
            Log("All done — Herd WoW is ready!");
            _installPath = wowRoot;
            await Task.Delay(600, ct);
            Invoke(() => ShowPage(_pageFinish));
        }
        catch (OperationCanceledException) { Log("Cancelled."); }
        catch (Exception ex) { Log($"ERROR: {ex.Message}"); SetStatus("Installation failed."); }
    }

    // ── Client manifest (lists the split archive parts on GitHub) ──────────────

    private class ClientManifestPart
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    private class ClientManifest
    {
        [JsonPropertyName("archiveName")]
        public string ArchiveName { get; set; } = "HerdWoW-Client.zip";

        [JsonPropertyName("parts")]
        public List<ClientManifestPart>? Parts { get; set; }

        public List<ClientManifestPart> GetParts()
        {
            return Parts ?? new List<ClientManifestPart>();
        }
    }

    private static async Task<ClientManifest> FetchClientManifestAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "HerdWoW-Installer/1.0");
        var json = await http.GetStringAsync(Config.ClientManifestUrl, ct);
        var manifest = JsonSerializer.Deserialize<ClientManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest == null || manifest.GetParts().Count == 0)
            throw new Exception("Client manifest is empty or invalid.");
        return manifest;
    }

    // Compute SHA256 hash of a file
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        var hash = await sha.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    // Concatenates split parts back into the original archive, in order —
    // matching how the launcher's patch manager reassembles multi-part patches.
    private static async Task AssembleFilesAsync(string targetPath, List<string> partPaths)
    {
        await using var outFile = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
        foreach (var part in partPaths)
        {
            await using var inFile = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            await inFile.CopyToAsync(outFile);
        }
    }

    // ── Generic download with retry + resume (used for client parts + launcher) ─

    private static async Task DownloadFileWithRetryAsync(
        string url, string destPath, CancellationToken ct,
        Action<int, string> progress, Action<string> log, string userAgent)
    {
        var tmp = destPath + ".download";
        Exception? lastErr = null;

        for (int attempt = 1; attempt <= DownloadMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            long resumeFrom = File.Exists(tmp) ? new FileInfo(tmp).Length : 0;
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", userAgent);
                http.Timeout = TimeSpan.FromMinutes(10);
                if (resumeFrom > 0)
                    http.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                bool resuming = resumeFrom > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (resumeFrom > 0 && resp.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    resuming = false;
                    resumeFrom = 0;
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                resp.EnsureSuccessStatusCode();

                long total = resuming
                    ? (resp.Content.Headers.ContentRange?.Length ?? (resumeFrom + (resp.Content.Headers.ContentLength ?? 0)))
                    : (resp.Content.Headers.ContentLength ?? -1L);
                long got = resuming ? resumeFrom : 0;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(tmp, resuming ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, true);

                var buf = new byte[81920];
                while (true)
                {
                    var readTask = stream.ReadAsync(buf, ct).AsTask();
                    if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(20), ct)) != readTask)
                        throw new IOException("Download stalled (no data received for 20s).");
                    int read = await readTask;
                    if (read == 0) break;

                    await file.WriteAsync(buf.AsMemory(0, read), ct);
                    got += read;
                    int pct = total > 0 ? (int)(got * 100 / total) : 0;
                    progress(pct, $"{Fmt(got)}{(total > 0 ? $" / {Fmt(total)}" : "")}");
                }

                await file.DisposeAsync();
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tmp, destPath);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastErr = ex;
                if (attempt >= DownloadMaxAttempts) break;
                var delay = TimeSpan.FromMilliseconds(Math.Min(30000, 1000 * Math.Pow(2, attempt - 1)));
                log($"Download interrupted ({ex.Message}) — retrying in {delay.TotalSeconds:F0}s…");
                await Task.Delay(delay, ct);
            }
        }

        throw lastErr ?? new Exception("Download failed.");
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private void Extract(string archivePath, string destDir)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        using var reader = archive.ExtractAllEntries();
        int done = 0;
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            var rel  = reader.Entry.Key
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var dst  = Path.Combine(destDir, rel);
            var dir  = Path.GetDirectoryName(dst)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using var es = reader.OpenEntryStream();
            using var fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            es.CopyTo(fs);
            done++;
            SetProgress(55 + done % 18, $"Extracting... ({done} files)");
        }
        SetProgress(73, $"Extracted {done} files.");
    }

    private static string? FindWowRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, "Wow.exe")) || File.Exists(Path.Combine(dir, "WoW.exe"))) return dir;
        foreach (var sub in Directory.GetDirectories(dir))
            if (File.Exists(Path.Combine(sub, "Wow.exe")) || File.Exists(Path.Combine(sub, "WoW.exe"))) return sub;
        return null;
    }

    // ── Shortcut ──────────────────────────────────────────────────────────────

    private static void CreateShortcut(string target, string workDir)
    {
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{Config.ShortcutName}.lnk");
        var ps   = $"$ws=$([Runtime.InteropServices.Marshal]::GetActiveObject('WScript.Shell') 2>$null) ?? (New-Object -ComObject WScript.Shell);" +
                   $"$s=$ws.CreateShortcut('{link}');" +
                   $"$s.TargetPath='{target}';" +
                   $"$s.WorkingDirectory='{workDir}';" +
                   $"$s.IconLocation='{target},0';" +
                   $"$s.Description='{Config.ServerName}';" +
                   $"$s.Save()";
        // Simpler powershell command
        var ps2 = $"$ws=New-Object -ComObject WScript.Shell;" +
                  $"$s=$ws.CreateShortcut('{link}');" +
                  $"$s.TargetPath='{target}';" +
                  $"$s.WorkingDirectory='{workDir}';" +
                  $"$s.IconLocation='{target},0';" +
                  $"$s.Description='{Config.ServerName}';" +
                  $"$s.Save()";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "powershell",
            Arguments       = $"-NoProfile -Command \"{ps2}\"",
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden,
            UseShellExecute = true
        })?.WaitForExit();
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void SetStatus(string text) { if (InvokeRequired) Invoke(() => SetStatus(text)); else _installStatus.Text = text; }

    private void SetProgress(int pct, string label)
    {
        if (InvokeRequired) { Invoke(() => SetProgress(pct, label)); return; }
        _progressBar.SetValue(Math.Clamp(pct, 0, 100));
        _progressLabel.Text = label;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { Invoke(() => Log(msg)); return; }
        _log.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
        _log.TopIndex = _log.Items.Count - 1;
    }

    private static string Fmt(long b) => b switch
    {
        < 1024             => $"{b} B",
        < 1024*1024        => $"{b/1024.0:F1} KB",
        < 1024L*1024*1024  => $"{b/(1024.0*1024):F1} MB",
        _                  => $"{b/(1024.0*1024*1024):F2} GB"
    };

    // ── Builder helpers ───────────────────────────────────────────────────────

    private Panel MakePage()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };
        p.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } };
        return p;
    }

    private void PageHeader(Panel page, string title, string sub)
    {
        var hdr = new Panel { Location = new Point(0, 0), Size = new Size(560, 90), BackColor = CARD };
        hdr.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } };
        hdr.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = TXT, AutoSize = true, Location = new Point(28, 18), BackColor = Color.Transparent });
        hdr.Controls.Add(new Label { Text = sub,   Font = new Font("Segoe UI",  9f),                 ForeColor = TXT2, AutoSize = true, Location = new Point(28, 58), BackColor = Color.Transparent });
        hdr.Controls.Add(DivLine(DockStyle.Bottom));
        page.Controls.Add(hdr);
    }

    private static Panel DivLine(DockStyle dock) =>
        new Panel { Dock = dock, Height = 1, BackColor = BORDER };

    private static Label AddLabel(Panel page, string text, int x, int y, int w, int h,
        bool bold = false, bool italic = false, Color? color = null)
    {
        var lbl = new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            Size      = new Size(w, h),
            ForeColor = color ?? Color.FromArgb(200, 205, 225),
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 9.5f,
                            bold   ? FontStyle.Bold   :
                            italic ? FontStyle.Italic : FontStyle.Regular)
        };
        page.Controls.Add(lbl);
        return lbl;
    }

    private Button DarkBtn(string text, int x, int y, int w, int h)
    {
        var b = new Button
        {
            Text      = text, Location = new Point(x, y), Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat, BackColor = BTN_DIM, ForeColor = TXT,
            Font      = new Font("Segoe UI", 9f), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = BORDER;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 42, 62);
        return b;
    }

    private Button NavBtn(string text, int rightOff)
    {
        var b = new Button
        {
            Text      = text,
            Size      = new Size(90, 32),
            Location  = new Point(Width - rightOff - 90, 12),
            FlatStyle = FlatStyle.Flat,
            BackColor = BTN_DIM,
            ForeColor = TXT,
            Font      = new Font("Segoe UI", 9.5f),
            Cursor    = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = BORDER;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 42, 62);
        return b;
    }

    private Panel ChromeBtn(int x, int y, bool isClose)
    {
        var p = new Panel { Location = new Point(x, y), Size = new Size(44, 32), BackColor = Color.Transparent, Cursor = Cursors.Hand };
        p.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(TXT2, 1.4f);
            if (isClose) { e.Graphics.DrawLine(pen, 15, 10, 29, 22); e.Graphics.DrawLine(pen, 29, 10, 15, 22); }
            else           e.Graphics.DrawLine(pen, 15, 16, 29, 16);
        };
        Color hover = isClose ? Color.FromArgb(160, 196, 30, 30) : Color.FromArgb(80, 60, 65, 90);
        p.MouseEnter += (s, e) => { p.BackColor = hover; p.Invalidate(); };
        p.MouseLeave += (s, e) => { p.BackColor = Color.Transparent; p.Invalidate(); };
        if (isClose) p.Click += (s, e) => Application.Exit();
        else         p.Click += (s, e) => WindowState = FormWindowState.Minimized;
        return p;
    }

    // ── Custom progress bar ───────────────────────────────────────────────────

    private class GradientBar : Panel
    {
        private int _val;
        public void SetValue(int v) { _val = Math.Clamp(v, 0, 100); Invalidate(); }
        public GradientBar() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(22, 26, 38));
            if (_val <= 0) return;
            int w = (int)(_val / 100.0 * Width);
            using var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(w, 1), Height),
                Color.FromArgb(0, 148, 255), Color.FromArgb(0, 210, 255), LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(b, 0, 0, w, Height);
        }
    }
}
