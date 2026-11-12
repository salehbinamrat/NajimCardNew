using System.Windows;
using System.Windows.Controls;

namespace NajmCard.Desktop.Views;

/// <summary>
/// First screen of User Manager; no API calls and no voucher side effects.
/// Six large, keyboard-accessible cards choose the destination explicitly.
/// </summary>
public partial class UserManagerHomeView : UserControl
{
    public Action<string>? Navigate { get; set; }

    public UserManagerHomeView()
    {
        InitializeComponent();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if(sender is Button {Tag:string destination})
            Navigate?.Invoke(destination);
    }
}
