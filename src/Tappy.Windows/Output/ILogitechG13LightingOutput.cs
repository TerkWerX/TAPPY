namespace Tappy.Windows.Output;

/// <summary>
/// Writes the one global RGB backlight zone exposed by a physical Logitech G13.
/// The persistent controller identity is required so an unrelated Logitech RGB
/// device, or a second G13, is never selected by model-wide guesswork.
/// </summary>
public interface ILogitechG13LightingOutput
{
    void SetBacklightRgb(string controllerPersistentId, byte red, byte green, byte blue);
}
