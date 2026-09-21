using System.Windows;
using System.Windows.Controls;
using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App;

public partial class BankControlWindow : Window
{
    private readonly ControllerAssignmentTarget _source;
    private bool _ready;
    private ControllerBankEndpoint? _displayedTarget;
    public Func<ControllerBankEndpoint?, BankControlBinding?, Task<RuntimeOperation>>? SaveBankControl { get; init; }
    public BankControlWindow(string sourceLabel, ControllerAssignmentTarget source,
        BankControlBinding? existing, IReadOnlyList<ControllerBankEndpoint> targets)
    {
        _source = source;
        InitializeComponent();
        SourceLabel.Text = $"Source button: {sourceLabel}";
        TargetBox.ItemsSource = targets;
        OperationBox.ItemsSource = new ControllerBankChoice[]
        { new("Next", "Next bank →"), new("Previous", "← Previous bank"), new("Select", "Go to bank…"), new("Toggle", "Toggle two banks…") };
        OperationBox.SelectedIndex = existing?.Operation switch
        { BankControlOperation.Previous => 1, BankControlOperation.Select => 2, BankControlOperation.Toggle => 3, _ => 0 };
        TargetBox.SelectedItem = existing is null
            ? targets.FirstOrDefault(t => t.ProviderId == source.ProviderId && t.PersistentId == source.ControllerPersistentId)
            : targets.FirstOrDefault(t => t.ProviderId == existing.TargetProviderId && t.PersistentId == existing.TargetPersistentId &&
                (!t.RequiresSessionBinding || t.ConnectionToken == existing.TargetConfirmationToken));
        RemoveButton.IsEnabled = existing is not null;
        if (existing is not null && TargetBox.SelectedItem is null)
        { StatusText.Text = $"Saved destination unavailable: {existing.TargetName}. Reconnect it, explicitly choose another destination, or remove this bank control."; }
        _ready = true;
        UpdateChoices();
        if (existing?.Operation is BankControlOperation.Select or BankControlOperation.Toggle)
        { BankBox.SelectedItem = (BankBox.ItemsSource as IReadOnlyList<ControllerBankChoice>)?.FirstOrDefault(b => b.Id == existing.LayerId); }
        if (existing?.Operation == BankControlOperation.Toggle)
        { AlternateBankBox.SelectedItem = (AlternateBankBox.ItemsSource as IReadOnlyList<ControllerBankChoice>)?.FirstOrDefault(b => b.Id == existing.AlternateLayerId); }
    }

    private void ChoiceChanged(object sender, SelectionChangedEventArgs e) { if (_ready) { UpdateChoices(); } }
    private void UpdateChoices()
    {
        var target = TargetBox.SelectedItem as ControllerBankEndpoint;
        if (!ReferenceEquals(target, _displayedTarget))
        {
            _displayedTarget = target;
            BankBox.ItemsSource = target?.Banks;
            BankBox.SelectedItem = target?.Banks.FirstOrDefault(b => b.Id == target.ActiveLayerId);
            AlternateBankBox.ItemsSource = target?.Banks;
            AlternateBankBox.SelectedItem = null;
        }
        var operation = (OperationBox.SelectedItem as ControllerBankChoice)?.Id;
        BankBox.IsEnabled = operation is "Select" or "Toggle";
        AlternateBankPanel.Visibility = operation == "Toggle" ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = target is not null;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (TargetBox.SelectedItem is not ControllerBankEndpoint target ||
            OperationBox.SelectedItem is not ControllerBankChoice operation) { return; }
        var kind = Enum.Parse<BankControlOperation>(operation.Id);
        if (kind is BankControlOperation.Select or BankControlOperation.Toggle && BankBox.SelectedItem is not ControllerBankChoice)
        { StatusText.Text = "Choose an available destination bank."; return; }
        if (kind == BankControlOperation.Toggle && (AlternateBankBox.SelectedItem is not ControllerBankChoice alternate ||
            alternate.Id == (BankBox.SelectedItem as ControllerBankChoice)?.Id))
        { StatusText.Text = "Choose a different second bank for the toggle."; return; }
        var binding = new BankControlBinding(new ControlId(_source.ControlId), target.ProviderId,
            target.PersistentId, target.DisplayName, kind, (BankBox.SelectedItem as ControllerBankChoice)?.Id ?? "",
            target.RequiresSessionBinding ? target.ConnectionToken : "",
            kind == BankControlOperation.Toggle ? ((ControllerBankChoice)AlternateBankBox.SelectedItem).Id : "");
        await SaveAsync(target, binding);
    }

    private async void RemoveClick(object sender, RoutedEventArgs e) => await SaveAsync(null, null);
    private async Task SaveAsync(ControllerBankEndpoint? target, BankControlBinding? binding)
    {
        SaveButton.IsEnabled = RemoveButton.IsEnabled = false;
        try
        {
            var result = SaveBankControl is null ? RuntimeOperation.Failed("No controller editor is attached.") :
                await SaveBankControl(target, binding).ConfigureAwait(true);
            StatusText.Text = result.Message;
            if (result.Succeeded) { DialogResult = true; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = "The change could not be saved. Your draft remains here. " + ex.Message; }
        finally { UpdateChoices(); RemoveButton.IsEnabled = true; }
    }
}
