using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscConstants
{
    public const string Referer = "https://www.aisichuang.net/";

    public const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/128.0 Safari/537.36";

    public const int TaskPageSize = 50;
    public const int TaskPageCap = 5;
    public const int PollIntervalMs = 2000;
    public const int PollTimeoutMs = 120_000;
    public const int FallbackTimeLimitMs = 2000;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
}

public class AscApiException : Exception
{
    public AscApiException(string message, long code = -1, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public long Code { get; }
}

public sealed class AscAuthException : AscApiException
{
    public AscAuthException(string message, long code = 401)
        : base(message, code)
    {
    }
}

/// <summary>
/// Client for the aisichuang.net tiku API. The site is built on Sa-Token:
/// the token travels in a literal "satoken" header plus "sessionid", and
/// state-changing calls echo a CSRF token in two header spellings.
///
/// The C++ branch in this repository is the reference; this class keeps the
/// same endpoint and field conventions, but never persists credentials.
/// </summary>
public sealed class AscClient : IDisposable
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _http;

    public AscClient(HttpMessageHandler? handler = null, string? baseUrl = null)
    {
        if (handler is null)
        {
            var socketHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                         DecompressionMethods.Brotli,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _http = new HttpClient(socketHandler, disposeHandler: true);
        }
        else
        {
            _http = new HttpClient(handler, disposeHandler: false);
        }

        _http.Timeout = AscConstants.RequestTimeout;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AscConstants.UserAgent);

        SetBaseUrl(baseUrl ?? AscSettings.DefaultBaseUrl);
    }

    public string BaseUrl { get; private set; } = AscSettings.DefaultBaseUrl;

    public AscSession Session { get; private set; } = new();

    public bool IsLoggedIn => Session.IsValid;

    public void SetBaseUrl(string baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl)
            ? AscSettings.DefaultBaseUrl
            : baseUrl.Trim();

        while (value.EndsWith('/'))
        {
            value = value[..^1];
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AscApiException("爱思创站点地址无效。");
        }

        BaseUrl = value;
    }

    public void SetSession(AscSession? session)
    {
        Session = session ?? new AscSession();
        if (string.IsNullOrEmpty(Session.BaseUrl))
        {
            Session.BaseUrl = BaseUrl;
        }
    }

    public void ClearSession()
    {
        Session = new AscSession { BaseUrl = BaseUrl, Role = "student" };
    }

    public async Task<AscSession> LoginAsync(
        string phone,
        string password,
        CancellationToken cancellationToken = default)
    {
        var csrf = string.Empty;

        // Some sites reject /api/profile before login; the C++ client treats
        // that as non-fatal and simply logs in without a CSRF token.
        try
        {
            var (profile, _) = await RequestAsync(
                HttpMethod.Get, "/api/profile", null, withCsrf: false, cancellationToken);
            if (profile.ValueKind == JsonValueKind.Object)
            {
                csrf = AscJson.Str(profile, "csrftoken");
            }
        }
        catch (AscAuthException)
        {
        }
        catch (AscApiException)
        {
        }

        var body = new JsonObject
        {
            ["phone"] = phone,
            ["password"] = password
        };

        var (data, _) = await RequestAsync(
            HttpMethod.Post,
            "/login/loginByPassword",
            body,
            withCsrf: true,
            cancellationToken,
            csrf);

        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new AscApiException("登录失败：站点没有返回会话。");
        }

        var token = string.Empty;
        if (AscJson.TryGet(data, "tokenInfo", out var tokenInfo) &&
            tokenInfo.ValueKind == JsonValueKind.Object)
        {
            token = AscJson.Str(tokenInfo, "tokenValue");
        }

        if (token.Length == 0)
        {
            token = AscJson.Str(data, "tokenValue", "token");
        }

        if (token.Length == 0)
        {
            throw new AscApiException("登录失败：站点没有发放 token。");
        }

        Session = new AscSession
        {
            BaseUrl = BaseUrl,
            Satoken = token,
            CsrfToken = AscJson.Str(data, "csrftoken"),
            SessionId = AscJson.Str(data, "sessionid", "sessionId"),
            UserId = AscJson.Str(data, "id", "userId"),
            Role = AscJson.Str(data, "role"),
            Phone = phone
        };

        if (string.IsNullOrWhiteSpace(Session.Role))
        {
            Session.Role = "student";
        }

        return Session;
    }

    public async Task<List<string>> ProbeSessionAsync(CancellationToken cancellationToken = default)
    {
        var (data, _) = await RequestAsync(
            HttpMethod.Get, "/api/languages", null, withCsrf: false, cancellationToken);

        var languages = new List<string>();
        foreach (var value in AscJson.Elements(data))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    languages.Add(text);
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var text = AscJson.Str(value, "name", "language", "label", "title");
                if (text.Length > 0)
                {
                    languages.Add(text);
                }
            }
        }

        return languages;
    }

    public async Task<(List<AscProblemRef> Problems, long Total)> SearchProblemsAsync(
        string keyword,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["page"] = page,
            ["pageSize"] = 20,
            ["title"] = keyword,
            ["role"] = string.IsNullOrWhiteSpace(Session.Role) ? "student" : Session.Role,
            ["userId"] = IdNode(Session.UserId),
            ["difficultyDenchmarking"] = null,
            ["difficultyRating"] = 0,
            ["knowledgeId"] = string.Empty,
            ["origin"] = string.Empty,
            ["level"] = 0,
            ["ruleType"] = string.Empty,
            ["course"] = string.Empty
        };

        var (data, _) = await RequestAsync(
            HttpMethod.Post, "/questionTk/getPage", body, withCsrf: true, cancellationToken);

        var problems = AscJson.ParseProblemRefs(data);
        var total = AscJson.Num(data, problems.Count, "total");
        return (problems, total);
    }

    public async Task<AscProblemRef> FindProblemAsync(
        string displayId,
        CancellationToken cancellationToken = default)
    {
        var (problems, _) = await SearchProblemsAsync(displayId, 1, cancellationToken);
        var match = problems.FirstOrDefault(problem =>
            string.Equals(problem.DisplayId, displayId, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new AscApiException($"题库中没有找到题目 {displayId}。");
    }

    public async Task<AscStatement> FetchStatementAsync(
        string displayId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var problem = await FindProblemAsync(displayId, cancellationToken);
        var firstError = string.Empty;

        try
        {
            var path = "/api/getProblemOjDto?problemId=" + Uri.EscapeDataString(problem.ProblemId);
            var (data, _) = await RequestAsync(
                HttpMethod.Get, path, null, withCsrf: false, cancellationToken);

            var statement = AscJson.ParseStatementOjDto(data, displayId, title);
            statement.ProblemId = problem.ProblemId;
            if (string.IsNullOrWhiteSpace(statement.DisplayId))
            {
                statement.DisplayId = problem.DisplayId;
            }

            if (string.IsNullOrWhiteSpace(statement.Title))
            {
                statement.Title = problem.Title;
            }

            return statement;
        }
        catch (AscAuthException)
        {
            throw;
        }
        catch (AscApiException ex)
        {
            firstError = ex.Message;
        }

        try
        {
            var body = new JsonObject
            {
                ["questionId"] = IdNode(problem.ProblemId),
                ["userId"] = IdNode(Session.UserId)
            };

            var (data, _) = await RequestAsync(
                HttpMethod.Post, "/questionTk/getDetailById", body, withCsrf: true, cancellationToken);

            var statement = AscJson.ParseStatementDetail(data, displayId, title);
            statement.ProblemId = problem.ProblemId;
            if (string.IsNullOrWhiteSpace(statement.DisplayId))
            {
                statement.DisplayId = problem.DisplayId;
            }

            if (string.IsNullOrWhiteSpace(statement.Title))
            {
                statement.Title = problem.Title;
            }

            return statement;
        }
        catch (AscAuthException)
        {
            throw;
        }
        catch (AscApiException ex)
        {
            throw new AscApiException(
                $"读取题面失败：{firstError}；备用接口也失败：{ex.Message}",
                ex.Code,
                ex);
        }
    }

    public async Task<List<AscClassInfo>> ListClassesAsync(CancellationToken cancellationToken = default)
    {
        var (data, _) = await RequestAsync(
            HttpMethod.Get,
            "/classroomAdmin/getClass?page=1&pageSize=50&search=",
            null,
            withCsrf: false,
            cancellationToken);

        return AscJson.ParseClasses(data);
    }

    public async Task<List<AscTaskInfo>> ListTasksAsync(
        AscProblemSource source,
        string classId,
        CancellationToken cancellationToken = default)
    {
        var isWork = source == AscProblemSource.Work;
        var endpoint = isWork ? "/student/getAllWork" : "/student/getAllTest";
        var items = new List<AscTaskInfo>();
        var total = -1L;

        for (var page = 1; page <= AscConstants.TaskPageCap; page++)
        {
            var query = $"?studentId={Uri.EscapeDataString(Session.UserId)}" +
                        $"&classId={Uri.EscapeDataString(classId)}" +
                        $"&page={page}&pageSize={AscConstants.TaskPageSize}";

            var (data, _) = await RequestAsync(
                HttpMethod.Get, endpoint + query, null, withCsrf: false, cancellationToken);

            var rows = AscJson.ParseTasks(data, source);
            items.AddRange(rows);

            if (data.ValueKind == JsonValueKind.Object)
            {
                total = AscJson.Num(data, total, "total");
            }

            if (rows.Count == 0)
            {
                break;
            }

            if (total >= 0 && items.Count >= total)
            {
                break;
            }
        }

        return items;
    }

    public async Task<List<AscTaskProblem>> ListTaskProblemsAsync(
        AscProblemSource source,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var isWork = source == AscProblemSource.Work;
        var endpoint = isWork ? "/student/getWorkList" : "/student/getTestList";
        var query = $"?userId={Uri.EscapeDataString(Session.UserId)}" +
                    (isWork ? $"&workId={Uri.EscapeDataString(taskId)}" : $"&testId={Uri.EscapeDataString(taskId)}");

        var (data, _) = await RequestAsync(
            HttpMethod.Get, endpoint + query, null, withCsrf: false, cancellationToken);

        return AscJson.ParseTaskProblems(data, source);
    }

    public async Task<string> SubmitCodeAsync(
        string code,
        string language,
        string problemId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["code"] = code,
            ["language"] = language,
            ["problem_id"] = IdNode(problemId)
        };

        var (data, message) = await RequestAsync(
            HttpMethod.Post, "/api/submission", body, withCsrf: true, cancellationToken);

        var submissionId = AscJson.SubmissionIdFrom(data, message);
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new AscApiException("站点接受了提交，但没有返回提交 id。");
        }

        return submissionId;
    }

    public async Task<AscSubmissionResult> PollSubmissionAsync(
        string submissionId,
        string problemId,
        CancellationToken cancellationToken = default)
    {
        // Never add usedTime here: the endpoint fails when that parameter exists.
        var query = $"?submission_id={Uri.EscapeDataString(submissionId)}" +
                    $"&studentId={Uri.EscapeDataString(Session.UserId)}" +
                    $"&tkId={Uri.EscapeDataString(problemId)}";

        var (data, _) = await RequestAsync(
            HttpMethod.Get, "/api/submission" + query, null, withCsrf: false, cancellationToken);

        return AscJson.ParseSubmission(data);
    }

    /// <summary>
    /// Reads the same detail payload the site's compileInfo page uses. Unlike
    /// the polling response this one carries per-test real_time/memory values,
    /// which is the only place the site exposes runtime and peak memory.
    /// </summary>
    public async Task<AscSubmissionResult> FetchSubmissionDetailAsync(
        string submissionId,
        CancellationToken cancellationToken = default)
    {
        var path = "/studentsSubmitTk/getOneBySubId?id=" + Uri.EscapeDataString(submissionId);
        var (data, _) = await RequestAsync(
            HttpMethod.Get, path, null, withCsrf: false, cancellationToken);

        var result = AscJson.ParseSubmission(data);
        if (string.IsNullOrWhiteSpace(result.Id))
        {
            result.Id = submissionId;
        }

        return result;
    }

    public async Task RegisterTestAnswerAsync(
        string testId,
        string questionId,
        AscSubmissionResult result,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["userid"] = IdNode(Session.UserId),
            ["testid"] = IdNode(testId),
            ["questionId"] = IdNode(questionId),
            ["isDo"] = 1,
            ["subTime"] = result.Time,
            ["isPass"] = (double)result.IsPass,
            ["records"] = 1,
            ["subStatic"] = result.RawStatus,
            ["score"] = result.Score,
            ["tkSubId"] = IdNode(result.Id)
        };

        await RequestAsync(
            HttpMethod.Post, "/student/submitTestAnswer", body, withCsrf: true, cancellationToken);
    }

    public async Task RegisterWorkAnswerAsync(
        string workId,
        string questionId,
        string code,
        string language,
        AscSubmissionResult result,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["id"] = IdNode(result.Id),
            ["isPass"] = (double)result.IsPass,
            ["userid"] = IdNode(Session.UserId),
            ["subCode"] = code,
            ["codeLanguage"] = language,
            ["workid"] = IdNode(workId),
            ["questionId"] = IdNode(questionId),
            ["score"] = result.Score,
            ["tkSubId"] = IdNode(result.Id)
        };

        await RequestAsync(
            HttpMethod.Post, "/student/submitWorkAnswer", body, withCsrf: true, cancellationToken);
    }

    private async Task<(JsonElement Data, string Message)> RequestAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        bool withCsrf,
        CancellationToken cancellationToken,
        string? csrfOverride = null)
    {
        if (!Uri.TryCreate(BaseUrl + path, UriKind.Absolute, out var uri))
        {
            throw new AscApiException("请求地址无效。");
        }

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Referrer = new Uri(AscConstants.Referer);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        if (Session.IsValid)
        {
            request.Headers.TryAddWithoutValidation("satoken", Session.Satoken);
            request.Headers.TryAddWithoutValidation("sessionid", Session.SessionId);
        }

        var csrf = string.IsNullOrWhiteSpace(csrfOverride) ? Session.CsrfToken : csrfOverride;
        if (withCsrf && !string.IsNullOrWhiteSpace(csrf))
        {
            request.Headers.TryAddWithoutValidation("X-CSRFToken", csrf);
            request.Headers.TryAddWithoutValidation("csrftoken", csrf);
        }

        if (body is not null)
        {
            request.Content = new StringContent(
                body.ToJsonString(RequestJsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AscApiException("无法连接爱思创站点：" + ex.Message, -1, ex);
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                var reason = response.IsSuccessStatusCode
                    ? "站点返回了无法解析的响应，可能接口已变化。"
                    : $"站点返回 HTTP {(int)response.StatusCode}。";
                throw new AscApiException(reason);
            }

            using (document)
            {
                var root = document.RootElement;

                if (AscJson.HasCode(root))
                {
                    if (!AscJson.IsSuccess(root, out var data, out var code, out var message))
                    {
                        if (AscJson.IsAuthFailure(code, message))
                        {
                            throw new AscAuthException(
                                string.IsNullOrWhiteSpace(message)
                                    ? "爱思创会话已过期，请重新登录。"
                                    : message,
                                code);
                        }

                        throw new AscApiException(
                            string.IsNullOrWhiteSpace(message)
                                ? $"站点返回错误（code {code}）。"
                                : $"站点返回：{message}（code {code}）",
                            code);
                    }

                    var cloned = data.ValueKind == JsonValueKind.Undefined ? default : data.Clone();
                    return (cloned, message);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new AscApiException($"站点返回 HTTP {(int)response.StatusCode}。");
                }

                return (root.Clone(), string.Empty);
            }
        }
    }

    private static JsonNode? IdNode(string? id)
    {
        var text = id?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return JsonValue.Create(0);
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(text);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
