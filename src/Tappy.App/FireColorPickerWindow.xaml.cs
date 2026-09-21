using System.Windows;
using System.Windows.Controls;

namespace Tappy.App;

/// <summary>
/// A minimal dark-themed RGB picker (three sliders plus a hex field) used for the
/// Akai Fire's full RGB pad lighting. Deliberately simple: no eyedropper, no color
/// history, no HSV wheel — the hardware just needs an arbitrary 24-bit color.
/// </summary>
public partial class FireColorPickerWindow : Window
{
    private bool _syncingFromSliders;
    private bool _syncingFromHex;

    public FireColorPickerWindow(byte initialRed, byte initialGreen, byte initialBlue)
    {
        InitializeComponent();
        RedSlider.Value = initialRed;
        GreenSlider.Value = initialGreen;
        BlueSlider.Value = initialBlue;
        UpdateFromSliders();
    }

    public byte SelectedRed { get; private set; }
    public byte SelectedGreen { get; private set; }
    public byte SelectedBlue { get; private set; }

    private void ColorSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingFromHex) { return; }
        UpdateFromSliders();
    }

    private void UpdateFromSliders()
    {
        _syncingFromSliders = true;
        try
        {
            var red = (byte)Math.Round(RedSlider.Value);
            var green = (byte)Math.Round(GreenSlider.Value);
            var blue = (byte)Math.Round(BlueSlider.Value);
            HexTextBox.Text = $"#{red:X2}{green:X2}{blue:X2}";
            PreviewBrush.Color = System.Windows.Media.Color.FromRgb(red, green, blue);
        }
        finally { _syncingFromSliders = false; }
    }

    private void HexTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromSliders) { return; }
        if (!ViewModels.MainViewModel.TryParseHexColor(HexTextBox.Text.Trim(), out var red, out var green, out var blue))
        {
            return;
        }

        _syncingFromHex = true;
        try
        {
            RedSlider.Value = red;
            GreenSlider.Value = green;
            BlueSlider.Value = blue;
            PreviewBrush.Color = System.Windows.Media.Color.FromRgb(red, green, blue);
        }
        finally { _syncingFromHex = false; }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedRed = (byte)Math.Round(RedSlider.Value);
        SelectedGreen = (byte)Math.Round(GreenSlider.Value);
        SelectedBlue = (byte)Math.Round(BlueSlider.Value);
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
