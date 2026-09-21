using System.Windows;
using MahApps.Metro.Controls;

namespace LocalHandler;

/// <summary>
/// The hand-off screen: the web POS sent an amount, the cashier reads it here in
/// letters four centimetres tall and types it into the bank's card terminal.
/// Topmost so it lands in front of whatever the till was showing.
/// </summary>
public partial class ChargeWindow : MetroWindow
{
    public ChargeWindow(decimal amount, string? reference)
    {
        InitializeComponent();
        AmountText.Text = $"{amount:0.000} OMR";
        RefText.Text = string.IsNullOrWhiteSpace(reference) ? "" : $"🧾 {reference}";
        System.Media.SystemSounds.Exclamation.Play();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
