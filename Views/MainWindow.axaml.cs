using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Simple_Doomsday_Engine_Launcher.Models;
using Simple_Doomsday_Engine_Launcher.ViewModels;

namespace Simple_Doomsday_Engine_Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

 

    }

    private void ServerList_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // --- FIXED: Abort tap routing immediately if an instance is running ---
            if (vm.IsGameRunning)
                return;

            // Execute the asynchronous task pool network routines safely
            _ = vm.ConnectSelectedServer();
        }
    }




}