using System.Text;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class SnippetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultPath =>
        Path.Combine(AppPaths.AppDataDirectory, "snippets.json");

    public static List<CodeSnippet> Load(string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
        {
            return CreateDefaults();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CodeSnippet>>(json, JsonOptions)
                   ?? new List<CodeSnippet>();
        }
        catch
        {
            return new List<CodeSnippet>();
        }
    }

    public static List<CodeSnippet> LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new List<CodeSnippet>();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CodeSnippet>>(json, JsonOptions)
                   ?? new List<CodeSnippet>();
        }
        catch
        {
            return new List<CodeSnippet>();
        }
    }

    public static void Save(IEnumerable<CodeSnippet> snippets, string? path = null)
    {
        path ??= DefaultPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snippets.ToList(), JsonOptions);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static List<CodeSnippet> CreateDefaults()
    {
        return new List<CodeSnippet>
        {
            new()
            {
                Name = "快读",
                Prefix = "read",
                Category = "基础",
                Body = """
                    inline int read() {
                        int x = 0, f = 1;
                        char c = getchar();
                        while (c < '0' || c > '9') {
                            if (c == '-') f = -1;
                            c = getchar();
                        }
                        while (c >= '0' && c <= '9') {
                            x = x * 10 + (c - '0');
                            c = getchar();
                        }
                        return x * f;
                    }

                    """
            },
            new()
            {
                Name = "并查集",
                Prefix = "dsu",
                Category = "数据结构",
                Body = """
                    struct DSU {
                        vector<int> parent, size;

                        explicit DSU(int n) : parent(n + 1), size(n + 1, 1) {
                            iota(parent.begin(), parent.end(), 0);
                        }

                        int find(int x) {
                            return parent[x] == x ? x : parent[x] = find(parent[x]);
                        }

                        bool unite(int a, int b) {
                            a = find(a);
                            b = find(b);
                            if (a == b) return false;
                            if (size[a] < size[b]) swap(a, b);
                            parent[b] = a;
                            size[a] += size[b];
                            return true;
                        }
                    };

                    """
            },
            new()
            {
                Name = "二分查找",
                Prefix = "lower_bound",
                Category = "基础",
                Body = """
                    int lowerBound(const vector<int>& a, int target) {
                        int left = 0, right = (int)a.size();
                        while (left < right) {
                            int mid = left + (right - left) / 2;
                            if (a[mid] < target) left = mid + 1;
                            else right = mid;
                        }
                        return left;
                    }

                    """
            },
            new()
            {
                Name = "邻接表",
                Prefix = "graph",
                Category = "图论",
                Body = """
                    struct Edge {
                        int to, weight;
                    };

                    vector<vector<Edge>> graph;

                    void addEdge(int from, int to, int weight = 1) {
                        graph[from].push_back({to, weight});
                        graph[to].push_back({from, weight});
                    }

                    """
            }
        };
    }
}
