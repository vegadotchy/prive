using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IatechShield.Gui;

/// <summary>
/// Petite fenêtre modale demandant un mot de passe / PIN. Construite en code pour
/// rester légère et réutilisable (protection anti-altération, déverrouillage).
/// </summary>
public sealed class PromptWindow : Window
{
    private readonly PasswordBox _box = new();
    private readonly TextBlock _error = new();

    public string Value => _box.Password;

    public PromptWindow(string title, string message, string okLabel = "Valider")
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;

        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x17, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 16
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });

        _box.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x23, 0x38));
        _box.Foreground = Brushes.White;
        _box.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xE8));
        _box.Padding = new Thickness(8);
        _box.FontSize = 14;
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };
        stack.Children.Add(_box);

        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
        _error.FontSize = 11;
        _error.Margin = new Thickness(0, 6, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        stack.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = new Button { Content = okLabel, Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        border.Child = stack;
        Content = border;
        Loaded += (_, _) => _box.Focus();
    }

    /// <summary>Affiche un message d'erreur sans fermer (mauvais code).</summary>
    public void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
        _box.Clear();
        _box.Focus();
    }

    private void Accept()
    {
        if (string.IsNullOrEmpty(_box.Password)) { ShowError("Saisie vide."); return; }
        DialogResult = true;
        Close();
    }
}
