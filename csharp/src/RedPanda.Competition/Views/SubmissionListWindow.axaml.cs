using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Views;

public partial class SubmissionListWindow : Window
{
    public SubmissionListWindow()
        : this(Array.Empty<AscSubmissionRecord>(), string.Empty, string.Empty)
    {
    }

    public SubmissionListWindow(
        IReadOnlyList<AscSubmissionRecord> records,
        string displayId,
        string nickname)
    {
        InitializeComponent();

        HeaderText.Text = string.IsNullOrWhiteSpace(nickname)
            ? displayId
            : $"{displayId} · {nickname}";
        SummaryText.Text = $"共 {records.Count} 条提交；双击或“查看详情”打开测试点。";
        SubmissionList.ItemsSource = records;
    }

    public AscSubmissionRecord? SelectedRecord { get; private set; }

    private void SubmissionList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DetailButton.IsEnabled = SubmissionList.SelectedItem is AscSubmissionRecord;
    }

    private async void SubmissionList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SubmissionList.SelectedItem is AscSubmissionRecord)
        {
            Accept();
            await Task.CompletedTask;
        }
    }

    private void Detail_Click(object? sender, RoutedEventArgs e)
    {
        Accept();
    }

    private void Accept()
    {
        if (SubmissionList.SelectedItem is not AscSubmissionRecord record)
        {
            return;
        }

        SelectedRecord = record;
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
