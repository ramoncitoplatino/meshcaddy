using System.Windows;

namespace MeshCaddy;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text.Trim();

    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value)) return;
        DialogResult = true;
    }
}
