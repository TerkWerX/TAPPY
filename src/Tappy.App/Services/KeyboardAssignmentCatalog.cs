using Tappy.App.Models;
using Tappy.Core.Output;

namespace Tappy.App.Services;

/// <summary>
/// Searchable, data-driven keyboard assignment catalog. Named Windows actions are
/// listed first; generated modifier combinations make the complete supported key
/// surface discoverable without hard-coding thousands of XAML rows.
/// </summary>
public static class KeyboardAssignmentCatalog
{
    public const string AllCategory = "All assignments";

    private static readonly string[][] ModifierSets =
    [
        ["CTRL"], ["ALT"], ["SHIFT"], ["WIN"],
        ["CTRL", "SHIFT"], ["CTRL", "ALT"], ["CTRL", "WIN"],
        ["ALT", "SHIFT"], ["ALT", "WIN"], ["SHIFT", "WIN"],
        ["CTRL", "ALT", "SHIFT"], ["CTRL", "ALT", "WIN"],
        ["CTRL", "SHIFT", "WIN"], ["ALT", "SHIFT", "WIN"],
        ["CTRL", "ALT", "SHIFT", "WIN"]
    ];

    public static IReadOnlyList<KeyboardAssignmentOption> Create()
    {
        var options = NamedActions().ToList();
        var allKeys = KeyboardOutputCapabilities.SupportedKeys.ToArray();
        var baseKeys = allKeys
            .Where(key => !IsModifier(key))
            .ToArray();

        options.AddRange(allKeys.Select(key => new KeyboardAssignmentOption(
            BaseCategory(key),
            FriendlyKeyName(key),
            FriendlyKey(key),
            $"Press the {FriendlyKey(key)} key.",
            [key])));

        foreach (var modifiers in ModifierSets)
        {
            var modifierLabel = string.Join(" + ", modifiers.Select(FriendlyKey));
            options.AddRange(baseKeys.Select(key =>
            {
                var keys = modifiers.Append(key).ToArray();
                var shortcut = string.Join(" + ", keys.Select(FriendlyKey));
                return new KeyboardAssignmentOption(
                    $"Key combinations · {modifierLabel}",
                    shortcut,
                    shortcut,
                    $"Send {shortcut} as one balanced keyboard chord.",
                    keys);
            }));
        }

        // Prefer the human-named action when a generated chord has the same keys.
        return options
            .GroupBy(option => string.Join("+", option.Keys), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public static IReadOnlyList<AssignmentCategory> CreateCategories(
        IReadOnlyCollection<KeyboardAssignmentOption> options) =>
    [
        new AssignmentCategory(AllCategory, options.Count),
        .. options.GroupBy(option => option.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssignmentCategory(group.Key, group.Count()))
    ];

    private static IEnumerable<KeyboardAssignmentOption> NamedActions()
    {
        (string Category, string Name, string Shortcut, string Description)[] actions =
        [
            ("Windows · Editing & files", "Copy", "CTRL+C", "Copy the selected item or text."),
            ("Windows · Editing & files", "Cut", "CTRL+X", "Cut the selected item or text."),
            ("Windows · Editing & files", "Paste", "CTRL+V", "Paste from the clipboard."),
            ("Windows · Editing & files", "Undo", "CTRL+Z", "Undo the last action."),
            ("Windows · Editing & files", "Redo", "CTRL+Y", "Redo the last action."),
            ("Windows · Editing & files", "Select all", "CTRL+A", "Select everything in the active view."),
            ("Windows · Editing & files", "Find", "CTRL+F", "Find text or items."),
            ("Windows · Editing & files", "Replace", "CTRL+H", "Open Replace in many applications."),
            ("Windows · Editing & files", "Save", "CTRL+S", "Save the active document."),
            ("Windows · Editing & files", "Save as", "CTRL+SHIFT+S", "Open Save As in many applications."),
            ("Windows · Editing & files", "Open", "CTRL+O", "Open a file."),
            ("Windows · Editing & files", "New", "CTRL+N", "Create a new document or window."),
            ("Windows · Editing & files", "Print", "CTRL+P", "Print the active document."),
            ("Windows · Editing & files", "Bold", "CTRL+B", "Toggle bold formatting in many editors."),
            ("Windows · Editing & files", "Italic", "CTRL+I", "Toggle italic formatting in many editors."),
            ("Windows · Editing & files", "Underline", "CTRL+U", "Toggle underline formatting in many editors."),

            ("Windows · System", "Show desktop", "WIN+D", "Show or restore the desktop."),
            ("Windows · System", "Open File Explorer", "WIN+E", "Open File Explorer."),
            ("Windows · System", "Open Settings", "WIN+I", "Open Windows Settings."),
            ("Windows · System", "Lock computer", "WIN+L", "Lock the current Windows session."),
            ("Windows · System", "Run command", "WIN+R", "Open the Run dialog."),
            ("Windows · System", "Windows Search", "WIN+S", "Open Windows Search."),
            ("Windows · System", "Quick Link menu", "WIN+X", "Open the power-user menu."),
            ("Windows · System", "Clipboard history", "WIN+V", "Open clipboard history."),
            ("Windows · System", "Emoji and symbols", "WIN+.", "Open the emoji and symbols panel."),
            ("Windows · System", "Screen snipping", "WIN+SHIFT+S", "Open the screen-snipping overlay."),
            ("Windows · System", "Task Manager", "CTRL+SHIFT+ESCAPE", "Open Task Manager."),
            ("Windows · System", "Switch apps", "ALT+TAB", "Move to the next open application."),
            ("Windows · System", "Switch apps backward", "ALT+SHIFT+TAB", "Move to the previous application."),
            ("Windows · System", "Close active window", "ALT+F4", "Close the active application window."),
            ("Windows · System", "Minimize all windows", "WIN+M", "Minimize all windows."),
            ("Windows · System", "Restore minimized windows", "WIN+SHIFT+M", "Restore windows minimized with Win+M."),
            ("Windows · System", "Snap window left", "WIN+LEFT", "Snap the active window to the left."),
            ("Windows · System", "Snap window right", "WIN+RIGHT", "Snap the active window to the right."),
            ("Windows · System", "Maximize window", "WIN+UP", "Maximize the active window."),
            ("Windows · System", "Minimize or restore window", "WIN+DOWN", "Minimize or restore the active window."),

            ("Windows · Virtual desktops", "New virtual desktop", "WIN+CTRL+D", "Create a virtual desktop."),
            ("Windows · Virtual desktops", "Desktop to the left", "WIN+CTRL+LEFT", "Switch to the desktop on the left."),
            ("Windows · Virtual desktops", "Desktop to the right", "WIN+CTRL+RIGHT", "Switch to the desktop on the right."),
            ("Windows · Virtual desktops", "Close virtual desktop", "WIN+CTRL+F4", "Close the current virtual desktop."),

            ("Windows · Browsers & tabs", "New tab", "CTRL+T", "Open a new browser tab."),
            ("Windows · Browsers & tabs", "Reopen closed tab", "CTRL+SHIFT+T", "Restore the most recently closed tab."),
            ("Windows · Browsers & tabs", "Close tab", "CTRL+W", "Close the current tab."),
            ("Windows · Browsers & tabs", "Next tab", "CTRL+TAB", "Move to the next tab."),
            ("Windows · Browsers & tabs", "Previous tab", "CTRL+SHIFT+TAB", "Move to the previous tab."),
            ("Windows · Browsers & tabs", "Address bar", "CTRL+L", "Focus the address bar."),
            ("Windows · Browsers & tabs", "Refresh", "CTRL+R", "Refresh the current page."),
            ("Windows · Browsers & tabs", "Hard refresh", "CTRL+SHIFT+R", "Refresh while bypassing cached content in many browsers."),
            ("Windows · Browsers & tabs", "Zoom in", "CTRL+SHIFT+=", "Increase page or document zoom."),
            ("Windows · Browsers & tabs", "Zoom out", "CTRL+-", "Decrease page or document zoom."),
            ("Windows · Browsers & tabs", "Reset zoom", "CTRL+0", "Reset page or document zoom."),
            ("Windows · Browsers & tabs", "Navigate back", "ALT+LEFT", "Navigate back."),
            ("Windows · Browsers & tabs", "Navigate forward", "ALT+RIGHT", "Navigate forward."),
            ("Windows · Browsers & tabs", "Downloads", "CTRL+J", "Open browser downloads."),
            ("Windows · Browsers & tabs", "History", "CTRL+H", "Open browser history."),

            ("Windows · Accessibility", "Magnifier", "WIN+=", "Open Magnifier or zoom in."),
            ("Windows · Accessibility", "Magnifier zoom out", "WIN+-", "Zoom Magnifier out."),
            ("Windows · Accessibility", "Close Magnifier", "WIN+ESCAPE", "Close Magnifier."),
            ("Windows · Accessibility", "Accessibility settings", "WIN+U", "Open Accessibility settings."),
            ("Windows · Accessibility", "Narrator", "WIN+CTRL+ENTER", "Toggle Narrator."),
            ("Windows · Accessibility", "Color filters", "WIN+CTRL+C", "Toggle color filters when enabled."),
            ("Windows · Accessibility", "High contrast", "LEFTALT+LEFTSHIFT+PRINTSCREEN", "Toggle the High Contrast prompt."),
            ("Windows · Accessibility", "Sticky Keys prompt", "SHIFT", "Press Shift; use hold behavior only when that is intentional."),

            ("Windows · Media & volume", "Play or pause", "MEDIAPLAYPAUSE", "Toggle media playback."),
            ("Windows · Media & volume", "Next track", "MEDIANEXT", "Skip to the next media track."),
            ("Windows · Media & volume", "Previous track", "MEDIAPREVIOUS", "Return to the previous media track."),
            ("Windows · Media & volume", "Stop playback", "MEDIASTOP", "Stop media playback."),
            ("Windows · Media & volume", "Volume up", "VOLUMEUP", "Raise system volume."),
            ("Windows · Media & volume", "Volume down", "VOLUMEDOWN", "Lower system volume."),
            ("Windows · Media & volume", "Mute volume", "VOLUMEMUTE", "Toggle system mute."),
            ("Windows · Media & volume", "Open media player", "MEDIASELECT", "Open the configured media application."),
            ("Windows · Media & volume", "Open mail", "LAUNCHMAIL", "Open the configured mail application."),

            ("Windows · Navigation", "Help", "F1", "Open help in many applications."),
            ("Windows · Navigation", "Rename selected item", "F2", "Rename the selected item in File Explorer."),
            ("Windows · Navigation", "Refresh key", "F5", "Refresh the active view."),
            ("Windows · Navigation", "Cycle screen elements", "F6", "Cycle through elements in the active window."),
            ("Windows · Navigation", "Full screen", "F11", "Toggle full-screen mode in many applications."),
            ("Windows · Navigation", "Context menu", "SHIFT+F10", "Open the context menu for the selected item."),
            ("Windows · Navigation", "Next field", "TAB", "Move to the next field or control."),
            ("Windows · Navigation", "Previous field", "SHIFT+TAB", "Move to the previous field or control."),
            ("Windows · Navigation", "Beginning of document", "CTRL+HOME", "Move to the beginning of a document."),
            ("Windows · Navigation", "End of document", "CTRL+END", "Move to the end of a document."),
            ("Windows · Navigation", "Delete previous word", "CTRL+BACKSPACE", "Delete the previous word in many editors."),
            ("Windows · Navigation", "Delete next word", "CTRL+DELETE", "Delete the next word in many editors.")
        ];

        return actions.Select(action => Option(
            action.Category, action.Name, action.Shortcut, action.Description));
    }

    private static KeyboardAssignmentOption Option(
        string category,
        string name,
        string shortcut,
        string description)
    {
        var keys = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(key => new KeyboardOutputKey(key).Value)
            .ToArray();
        return new KeyboardAssignmentOption(
            category,
            name,
            string.Join(" + ", keys.Select(FriendlyKey)),
            description,
            keys);
    }

    private static bool IsModifier(string key) => key is
        "SHIFT" or "LEFTSHIFT" or "RIGHTSHIFT" or
        "CTRL" or "LEFTCTRL" or "RIGHTCTRL" or
        "ALT" or "LEFTALT" or "RIGHTALT" or
        "WIN" or "LEFTWIN" or "RIGHTWIN";

    private static string BaseCategory(string key) => key switch
    {
        _ when IsModifier(key) => "Keys · Modifiers",
        [>= 'A' and <= 'Z'] => "Keys · Letters",
        [>= '0' and <= '9'] => "Keys · Number row",
        _ when key.StartsWith('F') && key.Length <= 3 => "Keys · Function keys",
        _ when key.StartsWith("NUMPAD", StringComparison.Ordinal) => "Keys · Numeric keypad",
        "LEFT" or "RIGHT" or "UP" or "DOWN" or "HOME" or "END" or "PAGEUP" or "PAGEDOWN" or
            "INSERT" or "DELETE" or "BACKSPACE" or "ENTER" or "TAB" or "SPACE" or "ESCAPE" =>
            "Keys · Navigation & editing",
        "CAPSLOCK" or "NUMLOCK" or "SCROLLLOCK" or "PRINTSCREEN" or "PAUSE" or "APPS" =>
            "Keys · Lock & system",
        "BROWSERBACK" or "BROWSERFORWARD" or "BROWSERREFRESH" or "BROWSERSTOP" or
            "BROWSERSEARCH" or "BROWSERFAVORITES" or "BROWSERHOME" or "LAUNCHMAIL" or
            "LAUNCHAPP1" or "LAUNCHAPP2" => "Keys · Browser & launch",
        "VOLUMEMUTE" or "VOLUMEDOWN" or "VOLUMEUP" or "MEDIANEXT" or "MEDIAPREVIOUS" or
            "MEDIASTOP" or "MEDIAPLAYPAUSE" or "MEDIASELECT" => "Keys · Media & volume",
        _ => "Keys · Punctuation & symbols"
    };

    private static string FriendlyKeyName(string key) => $"{FriendlyKey(key)} key";

    private static string FriendlyKey(string key) => key switch
    {
        "CTRL" => "Ctrl",
        "LEFTCTRL" => "Left Ctrl",
        "RIGHTCTRL" => "Right Ctrl",
        "SHIFT" => "Shift",
        "LEFTSHIFT" => "Left Shift",
        "RIGHTSHIFT" => "Right Shift",
        "ALT" => "Alt",
        "LEFTALT" => "Left Alt",
        "RIGHTALT" => "Right Alt",
        "WIN" => "Win",
        "LEFTWIN" => "Left Win",
        "RIGHTWIN" => "Right Win",
        "BACKSPACE" => "Backspace",
        "ENTER" => "Enter",
        "ESCAPE" => "Escape",
        "SPACE" => "Space",
        "PAGEUP" => "Page Up",
        "PAGEDOWN" => "Page Down",
        "PRINTSCREEN" => "Print Screen",
        "CAPSLOCK" => "Caps Lock",
        "NUMLOCK" => "Num Lock",
        "SCROLLLOCK" => "Scroll Lock",
        "NUMPADMULTIPLY" => "Numpad *",
        "NUMPADADD" => "Numpad +",
        "NUMPADSUBTRACT" => "Numpad -",
        "NUMPADDECIMAL" => "Numpad .",
        "NUMPADDIVIDE" => "Numpad /",
        "NUMPADENTER" => "Numpad Enter",
        "BROWSERBACK" => "Browser Back",
        "BROWSERFORWARD" => "Browser Forward",
        "BROWSERREFRESH" => "Browser Refresh",
        "BROWSERSTOP" => "Browser Stop",
        "BROWSERSEARCH" => "Browser Search",
        "BROWSERFAVORITES" => "Browser Favorites",
        "BROWSERHOME" => "Browser Home",
        "VOLUMEMUTE" => "Volume Mute",
        "VOLUMEDOWN" => "Volume Down",
        "VOLUMEUP" => "Volume Up",
        "MEDIANEXT" => "Media Next",
        "MEDIAPREVIOUS" => "Media Previous",
        "MEDIASTOP" => "Media Stop",
        "MEDIAPLAYPAUSE" => "Media Play/Pause",
        "MEDIASELECT" => "Media Select",
        "LAUNCHMAIL" => "Launch Mail",
        "LAUNCHAPP1" => "Launch App 1",
        "LAUNCHAPP2" => "Launch App 2",
        _ => key.Length == 1 || key.StartsWith('F') || key.StartsWith("NUMPAD", StringComparison.Ordinal)
            ? key
            : string.Concat(key[..1], key[1..].ToLowerInvariant())
    };
}
