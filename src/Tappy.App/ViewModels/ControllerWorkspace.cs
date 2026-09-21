using System.Collections.ObjectModel;
using System.Text.Json;
using Tappy.App.Runtime;
using Tappy.App.Services;

namespace Tappy.App.ViewModels;

public sealed class ControllerTab : ObservableObject
{
    internal ControllerTab(int slot, IControllerRuntime runtime, MainViewModel viewModel)
    {
        Slot = slot;
        Runtime = runtime;
        ViewModel = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ActiveControllerLabel) or nameof(MainViewModel.IsRehearsal) or nameof(MainViewModel.ControllerPhotoName))
            { Raise(nameof(Header)); }
        };
    }
    public int Slot { get; }
    internal IControllerRuntime Runtime { get; }
    public MainViewModel ViewModel { get; }
    internal bool SetupRequested { get; set; }
    internal FireOledContentPlayer OledContentPlayer { get; } = new();
    public string Header => $"Device {Slot} · {(ViewModel.HasControllerPhoto ? ViewModel.ControllerPhotoName : ViewModel.ActiveControllerLabel)} · {(ViewModel.IsRehearsal ? "Rehearsal" : "Normal output")}";
}

/// <summary>Tabs are independent live sessions. Changing the visible tab never re-arms a device.</summary>
public sealed partial class ControllerWorkspace : IAsyncDisposable
{
    private readonly Func<int, IControllerRuntime> _factory;
    private readonly Action<Action> _onUi;
    private readonly Action<TimeSpan, Action>? _onUiAfter;
    private readonly IAsyncDisposable? _resources;
    private readonly string? _settingsPath;
    private readonly string? _typingSettingsPath;
    private TypingKeyboardSettings _typingKeyboard = new(null, null);
    private bool _typingSettingsUnreadable;
    public ObservableCollection<ControllerTab> Tabs { get; } = [];
    // Hidden sessions stay alive for identity-safe reconnect, cleanup and saving.
    // Never reuse their slot/profile for a newly added controller.
    public ObservableCollection<ControllerTab> VisibleTabs { get; } = [];

    /// <summary>Shared across every tab by design — a saved animation is not scoped to any one device's own profile.</summary>
    public AnimationLibraryStore? AnimationLibrary { get; }
    public LedAnimationPlayer AnimationPlayer { get; } = new();

    public ControllerWorkspace(Func<int, IControllerRuntime> factory, Action<Action> onUi,
        Action<TimeSpan, Action>? onUiAfter = null, string? settingsPath = null,
        IAsyncDisposable? resources = null, AnimationLibraryStore? animationLibraryStore = null)
    {
        _factory = factory;
        _onUi = onUi;
        _onUiAfter = onUiAfter;
        _settingsPath = settingsPath;
        AnimationLibrary = animationLibraryStore;
        _typingSettingsPath = settingsPath is null ? null : settingsPath + ".typing-keyboard.json";
        try
        {
            if (_typingSettingsPath is not null) { _typingKeyboard = TypingKeyboardSettings.Load(_typingSettingsPath); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _typingSettingsUnreadable = true; }
        _resources = resources;
        var count = 1;
        try
        {
            if (settingsPath is not null && File.Exists(settingsPath))
            { count = Math.Clamp(JsonSerializer.Deserialize<int>(File.ReadAllText(settingsPath)), 1, 16); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        for (var index = 0; index < count; index++) { CreateTab(); }
    }

    private ControllerTab CreateTab()
    {
        var runtime = _factory(Tabs.Count + 1);
        runtime.SetTypingKeyboardProtection(_typingKeyboard.PersistentId, _typingSettingsUnreadable);
        var vm = new MainViewModel(runtime, _onUi, _onUiAfter);
        vm.TypingKeyboardStatus = TypingKeyboardStatus;
        var tab = new ControllerTab(Tabs.Count + 1, runtime, vm);
        vm.NormalOutputGuard = () =>
            Tabs.Any(other => !other.Runtime.IsOutputStateConfirmedSafe)
                ? "An output release could not be verified in another tab. Restart Tappy before enabling output."
                : Tabs.Any(other => other.Runtime.IsIdentificationCaptureActive)
                    ? "Finish or stop device identification before enabling output in any tab."
                    : null;
        vm.IdentificationGuard = choice =>
        {
            if (Tabs.Any(other => other != tab && other.Runtime.ReservedDevice is { } reserved &&
                reserved.ProviderId == choice.ProviderId &&
                (reserved.SessionId == choice.SessionId || reserved.PersistentId == choice.PersistentId)))
            { return "This controller already belongs to another tab. Select that tab to edit it."; }
            // Identification is a setup operation, never an opportunity for a
            // physical test press on another tab to execute its mapping.
            foreach (var other in Tabs) { other.ViewModel.IsRehearsal = true; }
            return null;
        };
        Tabs.Add(tab);
        _bankRuntimes.TryAdd(runtime, 0);
        runtime.BankControlRequested += Runtime_BankControlRequested;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(MainViewModel.IsWaitingForReconnect) or nameof(MainViewModel.HasActiveDeviceTab))) { return; }
            if (vm.HasActiveDeviceTab) { tab.SetupRequested = false; }
            UpdateTabVisibility(tab);
        };
        return tab;
    }

    private void UpdateTabVisibility(ControllerTab tab)
    {
        var visible = !tab.ViewModel.IsWaitingForReconnect && (tab.SetupRequested || tab.ViewModel.HasActiveDeviceTab);
        if (!visible) { VisibleTabs.Remove(tab); }
        else if (!VisibleTabs.Contains(tab))
        { VisibleTabs.Insert(VisibleTabs.Count(other => other.Slot < tab.Slot), tab); }
    }

    public async Task InitializeAsync()
    {
        foreach (var tab in Tabs) { await tab.ViewModel.InitializeAsync().ConfigureAwait(true); }
    }

    public async Task<ControllerTab> AddAsync()
    {
        // Startup profiles are hidden, not deleted. Reuse an idle saved slot only;
        // never take the slot of a disconnected device awaiting reconnection.
        var idle = Tabs.FirstOrDefault(tab => !VisibleTabs.Contains(tab) && tab.Runtime.ReservedDevice is null);
        if (idle is not null)
        {
            idle.SetupRequested = true;
            UpdateTabVisibility(idle);
            return idle;
        }
        if (Tabs.Count >= 16) { throw new InvalidOperationException("At most 16 device tabs can be open."); }
        var tab = CreateTab();
        try { await tab.ViewModel.InitializeAsync().ConfigureAwait(true); }
        catch
        {
            RemoveBankRouting(tab.Runtime);
            Tabs.Remove(tab);
            VisibleTabs.Remove(tab);
            await tab.ViewModel.DisposeAsync();
            throw;
        }
        tab.SetupRequested = true;
        UpdateTabVisibility(tab);
        if (_settingsPath is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var temporary = _settingsPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Tabs.Count));
                File.Move(temporary, _settingsPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { tab.ViewModel.ReportStatus($"Tab is open, but its startup setting could not be saved: {ex.Message}"); }
        }
        return tab;
    }

    public bool IsOutputStateConfirmedSafe => Tabs.All(tab => tab.ViewModel.IsOutputStateConfirmedSafe);
    private string TypingKeyboardStatus => _typingSettingsUnreadable
        ? "Typing protection needs attention — keyboard controllers blocked"
        : _typingKeyboard.PersistentId is null ? "Typing keyboard: not set"
        : $"Typing keyboard protected: {_typingKeyboard.Name}";

    public RuntimeOperation ProtectTypingKeyboard(MainViewModel viewModel)
    {
        var tab = Tabs.SingleOrDefault(item => item.ViewModel == viewModel);
        var verified = tab?.Runtime.GetVerifiedTypingKeyboard();
        if (verified is null || viewModel.SelectedDevice?.ProviderId != verified.ProviderId ||
            viewModel.SelectedDevice.SessionId != verified.SessionId)
        { return RuntimeOperation.Failed("Select your typing keyboard, click Identify, then press and release a key before protecting it. Do not confirm it as a controller."); }
        return SaveTypingKeyboard(new(verified.PersistentId, $"{verified.DisplayName} · ID {verified.DeviceTag}"));
    }

    public RuntimeOperation ClearTypingKeyboardProtection() => SaveTypingKeyboard(new(null, null));

    private RuntimeOperation SaveTypingKeyboard(TypingKeyboardSettings settings)
    {
        // All tabs remain in Rehearsal after changing protection. Never auto-arm a former primary.
        foreach (var tab in Tabs) { tab.ViewModel.IsRehearsal = true; }
        if (!IsOutputStateConfirmedSafe)
        { return RuntimeOperation.Failed("Output cleanup needs attention. Restart Tappy before changing typing-keyboard protection."); }
        try { if (_typingSettingsPath is not null) { settings.Save(_typingSettingsPath); } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return RuntimeOperation.Failed("Typing-keyboard protection could not be saved. Previous protection is unchanged; output remains in Rehearsal."); }
        _typingKeyboard = settings;
        _typingSettingsUnreadable = false;
        foreach (var tab in Tabs)
        {
            tab.Runtime.SetTypingKeyboardProtection(settings.PersistentId);
            tab.ViewModel.TypingKeyboardStatus = TypingKeyboardStatus;
        }
        return RuntimeOperation.Ok(settings.PersistentId is null
            ? "Typing-keyboard protection cleared. Controllers remain in Rehearsal."
            : "Typing keyboard protected in every tab. Normal Windows typing is unchanged; saved assignments are retained but cannot run on this keyboard.");
    }
    public bool IsIdentificationCaptureActive => Tabs.Any(tab => tab.ViewModel.IsIdentificationCaptureActive);
    public void Poll() { foreach (var tab in Tabs) { tab.ViewModel.PollRememberedReconnect(); } }
    public void TickOledContent(DateTimeOffset now) { foreach (var tab in Tabs) { tab.OledContentPlayer.Tick(tab, now); } }

    /// <summary>
    /// Sends the fixed OLED diagnostic pattern to every confirmed Fire, one
    /// tab at a time with no UI round-trip in between. Real hardware testing
    /// found the Fire's OLED reverts on its own a few seconds after Tappy
    /// stops sending updates (its own firmware idle behavior, not anything
    /// Tappy does), so seeing two units lit "at the same time" needs every
    /// send to land back-to-back like this, not one manual click per tab.
    /// </summary>
    public IReadOnlyList<RuntimeOperation> SendOledTestPatternToAllFires() =>
        Tabs.Where(tab => tab.Runtime.GetControllerLedColorCapability()?.ModelId == MidiControllerModelCatalog.AkaiFireV1Id)
            .Select(tab => tab.Runtime.SendFireOledDiagnosticPattern())
            .ToArray();
    public RuntimeOperation EmergencyStop(string reason)
    {
        var succeeded = true;
        foreach (var tab in Tabs) { succeeded &= tab.ViewModel.EmergencyStop(reason).Succeeded; }
        return succeeded ? RuntimeOperation.Ok("All device tabs stopped.") : RuntimeOperation.Failed("A device tab could not confirm output release.");
    }
    public async Task<bool> SaveAsync()
    {
        var succeeded = true;
        foreach (var tab in Tabs)
        { succeeded &= (await tab.ViewModel.SaveProfileForShutdownAsync().ConfigureAwait(false)).Succeeded; }
        return succeeded;
    }
    public async ValueTask DisposeAsync()
    {
        BankControlsPaused = true;
        AnimationPlayer.Stop();
        foreach (var tab in Tabs) { RemoveBankRouting(tab.Runtime); }
        foreach (var tab in Tabs) { await tab.ViewModel.DisposeAsync().ConfigureAwait(false); }
        if (_resources is not null) { await _resources.DisposeAsync().ConfigureAwait(false); }
    }
}
