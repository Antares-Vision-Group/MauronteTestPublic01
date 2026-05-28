using CommunityToolkit.Mvvm.ComponentModel;

namespace crossPlatform01.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
