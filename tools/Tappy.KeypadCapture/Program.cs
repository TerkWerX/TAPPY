using System.Text.Json;
using System.Text.RegularExpressions;
using Tappy.Windows.Input;

namespace Tappy.KeypadCapture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Developer visual QA only: construct/draw without Show, enumeration,
        // registration, an input target, or any capture session.
        if (args.Length == 2 && args[0] == "--render-preview" && Path.IsPathFullyQualified(args[1]))
        {
            ApplicationConfiguration.Initialize();
            using var preview = new CaptureWindow(new string('0', 64), Path.GetTempPath());
            preview.RenderPreview(args[1]);
            return;
        }
        if (args.Length != 4 || args[0] != "--target-fingerprint" || !Regex.IsMatch(args[1], "\\A[0-9A-F]{64}\\z") ||
            args[2] != "--output-folder" || !Path.IsPathFullyQualified(args[3]))
        {
            MessageBox.Show("Requires --target-fingerprint <exact SHA256> --output-folder <absolute path>. No capture was started.",
                "Tappy keypad diagnostic");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new CaptureWindow(args[1], args[3]));
    }
}

internal sealed class CaptureWindow : Form
{
    private readonly string _fingerprint;
    private readonly string _outputFolder;
    private readonly RawInputKeyboardProvider _provider = new(new NativeRawInputDeviceEnumerator(discoverWirelessNames: false), new RawInputMessageHost());
    private readonly Label _instructions = new() { AutoSize = false, Dock = DockStyle.Top, Height = 168 };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 105 };
    private readonly CheckBox _ack = new() { Text = "Tappy Rehearsal Mode is CHECKED; I will use only the Microsoft pad.", AutoSize = true };
    private readonly Button _advance = new() { Text = "Start pass 1", AutoSize = true, Enabled = false, TabStop = false };
    private readonly Button _abort = new() { Text = "Abort / close", AutoSize = true, TabStop = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private KeypadCaptureSession? _session;
    private SanitizedDeviceDescriptor? _target;
    private string? _savedPath;
    private bool _stopping;
    private bool _starting;
    private string? _notice;

    internal CaptureWindow(string fingerprint, string outputFolder)
    {
        _fingerprint = fingerprint; _outputFolder = outputFolder;
        Text = "Tappy — Microsoft Num Lock diagnostic v2 (no mapped output)";
        Font = new Font("Segoe UI", 12);
        ClientSize = new Size(840, 455);
        MinimumSize = new Size(840, 490);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White; ForeColor = Color.FromArgb(20, 28, 36);
        Padding = new Padding(22);
        KeyPreview = true;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55 };
        buttons.Controls.AddRange([_advance, _abort]);
        var consent = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45 };
        consent.Controls.Add(_ack);
        Controls.Add(buttons); Controls.Add(consent); Controls.Add(_status); Controls.Add(_instructions);
        _instructions.Text = "Two short passes, using the MOUSE for these buttons. No output is injected.\n" +
            "Press/release in order:  /  *  -   |   7  8  9  +   |   4  5  6   |   1  2  3  Enter   |   0  Decimal\n\n" +
            "Pass 1: use the current Num Lock state. Finish pass 1, toggle Num Lock ONCE, then start pass 2.\n" +
            "Only keypad scan codes / Windows key codes / counts are retained. No text or event history. Each pass stops after 2 minutes.";
        _status.Text = "Checking the exact selected Microsoft pad. No capture is armed.";
        _ack.CheckedChanged += (_, _) => UpdateState();
        _advance.Click += async (_, _) => await AdvanceAsync();
        _abort.Click += (_, _) => Close();
        _timer.Tick += (_, _) => { _session?.Tick(); UpdateState(); };
        _provider.IdentificationInputReceived += (_, e) => _session?.Observe(e.Input);
        _provider.DeviceChanged += (_, e) =>
        {
            if (KeypadCaptureConnectionGuard.StopReason(_target, e) is { } reason)
            { _session?.Abort(reason); _provider.ClearCaptureTarget(); }
        };
        _provider.LifecycleChanged += (_, e) =>
        {
            if (e.Signal is WindowsLifecycleSignal.SessionLocked or WindowsLifecycleSignal.Suspending or WindowsLifecycleSignal.ShutdownRequested or WindowsLifecycleSignal.Shutdown)
            { _session?.Abort("Session lock, sleep, or shutdown"); _provider.ClearCaptureTarget(); }
        };
        _provider.Faulted += (_, _) => { _session?.Abort("Input provider fault"); _provider.ClearCaptureTarget(); };
        Shown += (_, _) =>
        {
            try
            {
                _target = FindTarget();
                if (_target is null) { _status.Text = "Exact Microsoft pad unavailable or ambiguous. Nothing is armed. Close this window and reconnect the pad."; return; }
                _session = new KeypadCaptureSession(_target.PersistentId);
                _timer.Start(); UpdateState();
            }
            catch { _status.Text = "Device inventory failed. Nothing is armed. Close this window and try again."; }
        };
        FormClosing += OnClosing;
    }

    private SanitizedDeviceDescriptor? FindTarget()
    {
        var matches = _provider.EnumerateKeyboards().Where(d => d.PathFingerprintSha256 == _fingerprint &&
            d.PresentationModelId == KeyboardPresentationModels.MicrosoftBluetoothNumberPad).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task AdvanceAsync()
    {
        if (_session is null || !_ack.Checked || _starting || _stopping) { return; }
        _notice = null;
        _starting = true; _advance.Enabled = false;
        try
        {
            if (_session.IsCapturing)
            {
                if (!_session.Finish(_provider.IsCaptureTargetNeutral) && _session.IsCapturing)
                { _notice = "No keypad reports yet. Press/release the Microsoft pad's keys before finishing."; return; }
                _provider.ClearCaptureTarget();
            }
            else if (_session.Stage is CaptureStage.ReadyFirst or CaptureStage.ReadySecond)
            {
                await _provider.StartAsync();
                _target = FindTarget();
                if (_target is null) { _session.Abort("Exact target unavailable or ambiguous"); }
                else if (!_provider.SetCaptureTarget(_target.SessionHandle))
                { _notice = "Release ALL keyboard keys, then click Start again with the mouse. Nothing is armed."; return; }
                else { _session.Start(); }
            }
        }
        catch { _session.Abort("Unable to start input capture"); _provider.ClearCaptureTarget(); }
        finally { _starting = false; UpdateState(); }
    }

    private void UpdateState()
    {
        if (_session is null || _starting || _stopping || IsDisposed) { return; }
        var snapshot = _session.Snapshot();
        var counts = string.Join("   ", snapshot.Passes.Select(p => $"Pass {p.Pass}: {p.AcceptedPackets} packets / {p.Reports.Select(r => r.ScanCode).Distinct().Count()} key positions"));
        _ack.Enabled = !_session.IsCapturing;
        _advance.Enabled = _session.IsCapturing || (_ack.Checked && _session.Stage is CaptureStage.ReadyFirst or CaptureStage.ReadySecond);
        _advance.Text = _session.Stage switch
        {
            CaptureStage.ReadyFirst => "Start pass 1",
            CaptureStage.First => "Finish pass 1",
            CaptureStage.ReadySecond => "Start pass 2",
            CaptureStage.Second => "Finish and save",
            CaptureStage.Aborted => "Capture stopped",
            _ => "Finished"
        };
        _status.ForeColor = _session.Stage == CaptureStage.Aborted ? Color.DarkRed : Color.FromArgb(20, 28, 36);
        _status.Text = _session.Stage switch
        {
            CaptureStage.ReadyFirst => "READY — capture is OFF. Check the rehearsal confirmation, then click Start pass 1.",
            CaptureStage.ReadySecond => $"PASS 1 SAVED IN MEMORY — capture is OFF.\nToggle the pad's Num Lock ONCE; release it; click Start pass 2.\n{counts}",
            CaptureStage.First or CaptureStage.Second => $"CAPTURING ONLY THE MICROSOFT PAD — {_advance.Text} when your sweep is done.\n{counts}\nUse the mouse to finish; release every key first.",
            _ => $"STOPPED — {snapshot.StopReason}\n{counts}"
        };
        if (_notice is not null) { _status.Text = _notice + "\n" + counts; }
        if (_session.Stage is CaptureStage.Completed or CaptureStage.Aborted)
        {
            _provider.ClearCaptureTarget();
            _timer.Stop();
            SaveSummary();
        }
    }

    private void SaveSummary()
    {
        if (_session is null || _savedPath is not null) { return; }
        try
        {
            Directory.CreateDirectory(_outputFolder);
            var path = Path.Combine(_outputFolder, $"microsoft-numlock-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new
            {
                SchemaVersion = 1,
                Tool = "Tappy.KeypadCapture",
                ToolRevision = 2,
                DeviceFingerprint = _fingerprint,
                Model = KeyboardPresentationModels.MicrosoftBluetoothNumberPad,
                Privacy = "Selected keypad aggregate counts only; no text, event chronology, other-device reports, or injected output.",
                Result = _session.Snapshot()
            }, new JsonSerializerOptions { WriteIndented = true });
            _savedPath = path;
            _status.Text += "\nSummary saved. Tell Codex: capture finished. You can close this window.";
        }
        catch { _status.Text += "\nCould not save the summary. Leave this window open and tell Codex."; }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_stopping) { return; }
        e.Cancel = true; _stopping = true;
        _timer.Stop(); _session?.Abort("Closed by operator"); _provider.ClearCaptureTarget();
        SaveSummary();
        try { await _provider.DisposeAsync(); }
        finally { _timer.Dispose(); Close(); }
    }

    internal void RenderPreview(string path)
    {
        _status.Text = "READY — capture is OFF. Check the rehearsal confirmation, then click Start pass 1.";
        CreateControl(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        // Unshown parents make child.Visible false. Render each actual control
        // independently instead of showing a window merely to obtain a preview.
        using var graphics = Graphics.FromImage(bitmap);
        var clientOffset = new Point((Width - ClientSize.Width) / 2, Height - ClientSize.Height - (Width - ClientSize.Width) / 2);
        void DrawChildren(Control parent, Point offset)
        {
            parent.PerformLayout();
            foreach (Control child in parent.Controls)
            {
                if (child.Width <= 0 || child.Height <= 0) { continue; }
                using var image = new Bitmap(child.Width, child.Height);
                child.DrawToBitmap(image, new Rectangle(Point.Empty, child.Size));
                var position = new Point(offset.X + child.Left, offset.Y + child.Top);
                graphics.DrawImageUnscaled(image, position);
                DrawChildren(child, position);
            }
        }
        DrawChildren(this, clientOffset);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Native pass-through must not click Finish/Start or move UI focus. Mouse only.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); }
        return true;
    }
    protected override bool ProcessDialogKey(Keys keyData) => true;
    protected override void OnKeyDown(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }
    protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }
}
