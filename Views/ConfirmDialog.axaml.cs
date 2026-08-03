using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Simple_Doomsday_Engine_Launcher;


public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string message, bool showYesNo = true)
    {
        InitializeComponent();

        this.FindControl<TextBlock>("MessageText").Text = message;

        var yesButton = this.FindControl<Button>("YesButton");
        var noButton = this.FindControl<Button>("NoButton");

        if (!showYesNo)
        {
            yesButton.IsVisible = false;
            noButton.Content = "OK";
        }

        yesButton.Click += (_, __) =>
        {
            Close(true);
        };

        noButton.Click += (_, __) =>
        {
            Close(false);
        };
    }
}

