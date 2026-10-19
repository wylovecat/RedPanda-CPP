using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel
}

public enum ImportModeChoice
{
    Append,
    Replace,
    Cancel
}

public static class Dialogs
{
    public static Task ShowMessageAsync(Window owner, string title, string message)
    {
        return ShowChoiceAsync(owner, title, message, 0, 0, "确定");
    }

    /// <summary>
    /// A read-only, scrollable view for long reports such as test-case lists.
    /// </summary>
    public static async Task ShowDetailsAsync(Window owner, string title, string details)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var dialog = new Window
        {
            Title = title,
            Width = 680,
            Height = 520,
            MinWidth = 460,
            MinHeight = 360,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(
                Color.Parse(ThemeService.IsDark ? "#24262A" : "#FFFFFF"))
        };

        var panel = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,12,Auto")
        };

        var box = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            Background = new SolidColorBrush(
                Color.Parse(ThemeService.IsDark ? "#1B1D20" : "#F7F7F9")),
            Padding = new Thickness(10)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        var close = new Button
        {
            Content = "关闭",
            MinWidth = 84,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        close.Classes.Add("accent");

        panel.Children.Add(box);
        Grid.SetRow(close, 2);
        panel.Children.Add(close);
        dialog.Content = panel;

        close.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, _) => completion.TrySetResult(true);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    completion.TrySetResult(true);
                    dialog.Close();
                }
            },
            RoutingStrategies.Tunnel);

        await dialog.ShowDialog(owner);
        await completion.Task;
    }

    public static async Task<ImportModeChoice> AskImportModeAsync(
        Window owner,
        int importedCount,
        int existingCount)
    {
        var index = await ShowChoiceAsync(
            owner,
            "导入测试点",
            $"将导入 {importedCount} 个测试点。当前已有 {existingCount} 个测试点，要追加还是替换？",
            0,
            2,
            "追加",
            "替换",
            "取消");

        return index switch
        {
            0 => ImportModeChoice.Append,
            1 => ImportModeChoice.Replace,
            _ => ImportModeChoice.Cancel
        };
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "确定",
        string cancelText = "取消")
    {
        var index = await ShowChoiceAsync(owner, title, message, 0, 1, confirmText, cancelText);
        return index == 0;
    }

    public static async Task<bool> AskRestoreDraftAsync(Window owner, string displayName)
    {
        var index = await ShowChoiceAsync(
            owner,
            "恢复未保存的草稿",
            $"发现“{displayName}”的未保存草稿。恢复后可以继续编辑；选择丢弃将删除该草稿。",
            0,
            1,
            "恢复",
            "丢弃");

        return index == 0;
    }

    public static async Task<SaveChangesChoice> AskSaveChangesAsync(Window owner, string displayName)
    {
        var index = await ShowChoiceAsync(
            owner,
            "未保存的修改",
            $"“{displayName}”有未保存的修改。",
            0,
            2,
            "保存",
            "不保存",
            "取消");

        return index switch
        {
            0 => SaveChangesChoice.Save,
            1 => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel
        };
    }

    private static async Task<int> ShowChoiceAsync(
        Window owner,
        string title,
        string message,
        int defaultIndex,
        int cancelIndex,
        params string[] buttons)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(
                Color.Parse(ThemeService.IsDark ? "#24262A" : "#FFFFFF"))
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18
        };

        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 84,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            if (i == defaultIndex)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) =>
            {
                completion.TrySetResult(index);
                dialog.Close();
            };

            buttonPanel.Children.Add(button);
        }

        content.Children.Add(buttonPanel);
        dialog.Content = content;

        dialog.Closing += (_, _) => completion.TrySetResult(cancelIndex);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    completion.TrySetResult(cancelIndex);
                    dialog.Close();
                }
            },
            RoutingStrategies.Tunnel);

        await dialog.ShowDialog(owner);
        return completion.Task.IsCompleted ? await completion.Task : cancelIndex;
    }
}
