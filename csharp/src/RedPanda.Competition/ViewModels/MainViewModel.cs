using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<TestCase> TestCases { get; } = new();

    [ObservableProperty]
    private TestCase? _selectedCase;
}
