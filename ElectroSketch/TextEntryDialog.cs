using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ElectroSketch;

public sealed class TextEntryDialog : Window
{
    private readonly TextBox _textBox;
    public string AnnotationText => _textBox.Text;

    public TextEntryDialog(string initialText = "", bool editing = false)
    {
        Title = editing ? "Edit text on playground" : "Add text to playground";
        Width = 390;
        Height = 190;
        MinWidth = 320;
        MinHeight = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "Text", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });

        _textBox = new TextBox
        {
            Text = initialText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        Grid.SetRow(_textBox, 1);
        root.Children.Add(_textBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = editing ? "Save changes" : "Add text", MinWidth = 90, IsDefault = true };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text)) return;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => Keyboard.Focus(_textBox);
    }
}
