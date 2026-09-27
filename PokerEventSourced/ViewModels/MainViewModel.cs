using CommunityToolkit.Mvvm.ComponentModel;

namespace PokerEventSourced.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Status { get; set; } = "No hands imported yet.";
}
