using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public partial class SnippetWindow : Window
{
    private readonly List<CodeSnippet> _snippets;
    private CodeSnippet? _selected;
    private bool _loadingSelection;
    private bool _loadingCategories;
    private string _categoryFilter = "全部分类";

    public SnippetWindow()
        : this(null, false)
    {
    }

    public SnippetWindow(string? initialBody, bool createNew)
    {
        InitializeComponent();

        _snippets = SnippetStore.Load();
        RefreshCategories();
        RefreshList();

        if (createNew)
        {
            BeginNew(initialBody);
        }
        else
        {
            SnippetList.SelectedIndex = _snippets.Count > 0 ? 0 : -1;
        }
    }

    public CodeSnippet? InsertRequested { get; private set; }

    public bool SavedSomething { get; private set; }

    private static readonly FilePickerFileType SnippetFileType = new("代码片段 JSON")
    {
        Patterns = new[] { "*.json" }
    };

    private void RefreshCategories()
    {
        var categories = new List<string> { "全部分类" };
        categories.AddRange(_snippets
            .Select(snippet => snippet.Category?.Trim() ?? string.Empty)
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category));

        _loadingCategories = true;
        try
        {
            CategoryFilterCombo.ItemsSource = categories;
            CategoryFilterCombo.SelectedItem = categories.Contains(_categoryFilter)
                ? _categoryFilter
                : categories[0];
            _categoryFilter = CategoryFilterCombo.SelectedItem as string ?? categories[0];
        }
        finally
        {
            _loadingCategories = false;
        }
    }

    private void RefreshList()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = _snippets.Where(snippet =>
                (_categoryFilter == "全部分类" ||
                 string.Equals(snippet.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 ||
                 snippet.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 snippet.Prefix.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 snippet.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 snippet.Body.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _loadingSelection = true;
        try
        {
            SnippetList.ItemsSource = null;
            SnippetList.ItemsSource = filtered;
            SnippetList.SelectedItem = _selected is not null && filtered.Contains(_selected)
                ? _selected
                : null;
        }
        finally
        {
            _loadingSelection = false;
        }

        StatusText.Text = filtered.Count == 0
            ? "没有匹配的片段。"
            : $"共 {filtered.Count} 个片段；修改后点“保存片段”。";
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void CategoryFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingCategories)
        {
            return;
        }

        _categoryFilter = CategoryFilterCombo.SelectedItem as string ?? "全部分类";
        RefreshList();
    }

    private void SnippetList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSelection || SnippetList.SelectedItem is not CodeSnippet snippet)
        {
            return;
        }

        _selected = snippet;
        NameBox.Text = snippet.Name;
        PrefixBox.Text = snippet.Prefix;
        CategoryBox.Text = snippet.Category;
        BodyBox.Text = snippet.Body;
        StatusText.Text = "已加载，可修改后保存。";
    }

    private void New_Click(object? sender, RoutedEventArgs e)
    {
        BeginNew(null);
    }

    private void BeginNew(string? initialBody)
    {
        _selected = null;
        SnippetList.SelectedItem = null;
        NameBox.Text = string.Empty;
        PrefixBox.Text = string.Empty;
        CategoryBox.Text = "常用";
        BodyBox.Text = initialBody ?? string.Empty;
        StatusText.Text = string.IsNullOrWhiteSpace(initialBody)
            ? "正在新建片段；填写名称和内容后保存。"
            : "已带入选中代码；填写名称后保存。";
        NameBox.Focus();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var body = BodyBox.Text ?? string.Empty;

        if (name.Length == 0)
        {
            StatusText.Text = "请填写片段名称。";
            NameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            StatusText.Text = "片段内容不能为空。";
            BodyBox.Focus();
            return;
        }

        var prefix = PrefixBox.Text?.Trim() ?? string.Empty;
        var category = CategoryBox.Text?.Trim() ?? string.Empty;
        if (category.Length == 0)
        {
            category = "常用";
        }

        if (_selected is null)
        {
            _selected = new CodeSnippet
            {
                Name = name,
                Prefix = prefix,
                Category = category,
                Body = body
            };
            _snippets.Add(_selected);
        }
        else
        {
            _selected.Name = name;
            _selected.Prefix = prefix;
            _selected.Category = category;
            _selected.Body = body;
            _selected.UpdatedAtUtc = DateTime.UtcNow;
        }

        try
        {
            SnippetStore.Save(_snippets);
            SavedSomething = true;
            RefreshCategories();
            _categoryFilter = "全部分类";
            CategoryFilterCombo.SelectedItem = _categoryFilter;
            RefreshList();
            SnippetList.SelectedItem = _selected;
            StatusText.Text = $"已保存“{name}”。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            StatusText.Text = "请先选择要删除的片段。";
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            this,
            "删除代码片段",
            $"确定删除“{_selected.Name}”吗？",
            "删除",
            "取消");
        if (!confirmed)
        {
            return;
        }

        _snippets.Remove(_selected);
        _selected = null;

        try
        {
            SnippetStore.Save(_snippets);
            NameBox.Text = string.Empty;
            PrefixBox.Text = string.Empty;
            CategoryBox.Text = "常用";
            BodyBox.Text = string.Empty;
            RefreshCategories();
            RefreshList();
            StatusText.Text = "已删除。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "删除失败：" + ex.Message;
        }
    }

    private void Insert_Click(object? sender, RoutedEventArgs e)
    {
        var body = BodyBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            StatusText.Text = "没有可插入的内容。";
            return;
        }

        InsertRequested = new CodeSnippet
        {
            Id = _selected?.Id ?? string.Empty,
            Name = NameBox.Text?.Trim() ?? _selected?.Name ?? "片段",
            Prefix = PrefixBox.Text?.Trim() ?? _selected?.Prefix ?? string.Empty,
            Category = CategoryBox.Text?.Trim() ?? _selected?.Category ?? "常用",
            Body = body
        };
        Close();
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入代码片段",
            AllowMultiple = false,
            FileTypeFilter = new[] { SnippetFileType }
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        var imported = SnippetStore.LoadFile(path);
        if (imported.Count == 0)
        {
            StatusText.Text = "没有可导入的片段。";
            return;
        }

        foreach (var snippet in imported)
        {
            snippet.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(snippet.Category))
            {
                snippet.Category = "常用";
            }

            _snippets.Add(snippet);
        }

        try
        {
            SnippetStore.Save(_snippets);
            RefreshCategories();
            RefreshList();
            StatusText.Text = $"已导入 {imported.Count} 个片段。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出代码片段",
            SuggestedFileName = "snippets.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { SnippetFileType }
        });

        if (file is null || file.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            SnippetStore.Save(_snippets, path);
            StatusText.Text = $"已导出 {_snippets.Count} 个片段。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "导出失败：" + ex.Message;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
