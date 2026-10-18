using System.Net;
using System.Net.Http;
using System.Text;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscClientTests
{
    [Fact]
    public async Task Login_FetchesCsrfThenPostsCredentialsAndParsesSession()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":200,"msg":"ok","data":{"csrftoken":"csrf-from-profile"}}"""),
            Json("""
                {
                  "code":200,
                  "msg":"ok",
                  "data":{
                    "tokenInfo":{"tokenValue":"token-123"},
                    "csrftoken":"csrf-after-login",
                    "sessionid":"session-1",
                    "id":42,
                    "role":"student"
                  }
                }
                """));

        using var client = new AscClient(handler, "https://example.test/tiku");
        var session = await client.LoginAsync("13800000000", "secret-password");

        Assert.Equal("token-123", session.Satoken);
        Assert.Equal("csrf-after-login", session.CsrfToken);
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal("42", session.UserId);
        Assert.Equal("student", session.Role);
        Assert.True(client.IsLoggedIn);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("/api/profile", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/login/loginByPassword", handler.Requests[1].Uri.AbsolutePath);
        Assert.Contains("\"phone\":\"13800000000\"", handler.Requests[1].Body);
        Assert.Contains("\"password\":\"secret-password\"", handler.Requests[1].Body);
        Assert.Equal("csrf-from-profile", handler.Requests[1].Header("csrftoken"));
        Assert.Equal("https://www.aisichuang.net/", handler.Requests[1].Header("Referer"));
    }

    [Fact]
    public async Task SubmitCode_UsesAuthAndCsrfHeadersAndReturnsSubmissionId()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":200,"msg":"9999","data":null}"""));

        using var client = new AscClient(handler, "https://example.test/tiku");
        client.SetSession(new AscSession
        {
            BaseUrl = "https://example.test/tiku",
            Satoken = "tok",
            CsrfToken = "csrf",
            SessionId = "sid",
            UserId = "42",
            Role = "student"
        });

        var id = await client.SubmitCodeAsync("int main(){}", "C++", "123");

        Assert.Equal("9999", id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/submission", request.Uri.AbsolutePath);
        Assert.Equal("tok", request.Header("satoken"));
        Assert.Equal("sid", request.Header("sessionid"));
        Assert.Equal("csrf", request.Header("csrftoken"));
        Assert.Equal("csrf", request.Header("X-CSRFToken"));
        Assert.Contains("\"language\":\"C++\"", request.Body);
        Assert.Contains("\"problem_id\":123", request.Body);
    }

    [Fact]
    public async Task ListTaskProblems_UsesCorrectIdParameter()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":200,"data":{"records":[{"quesTypeId":5,"displayId":"A1","title":"A+B"}]}}"""));

        using var client = new AscClient(handler, "https://example.test/tiku");
        client.SetSession(new AscSession
        {
            BaseUrl = "https://example.test/tiku",
            Satoken = "tok",
            UserId = "42",
            Role = "student"
        });

        var problems = await client.ListTaskProblemsAsync(AscProblemSource.Work, "55");

        Assert.Single(problems);
        Assert.Equal("A1", problems[0].DisplayId);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("workId=55", request.Uri.Query);
        Assert.Contains("userId=42", request.Uri.Query);
        Assert.EndsWith("/student/getWorkList", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task RegisterWorkAnswer_SendsCodeAndIds()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":200,"msg":"ok","data":null}"""));

        using var client = new AscClient(handler, "https://example.test/tiku");
        client.SetSession(new AscSession
        {
            BaseUrl = "https://example.test/tiku",
            Satoken = "tok",
            CsrfToken = "csrf",
            UserId = "42",
            Role = "student"
        });

        await client.RegisterWorkAnswerAsync(
            "55",
            "123",
            "int main(){}",
            "C++",
            new AscSubmissionResult { Id = "999", IsPass = 1, RawStatus = "答案正确", Score = 100 });

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/student/submitWorkAnswer", request.Uri.AbsolutePath);
        Assert.Contains("\"workid\":55", request.Body);
        Assert.Contains("\"questionId\":123", request.Body);
        Assert.Contains("\"subCode\":\"int main(){}\"", request.Body);
        Assert.Contains("\"codeLanguage\":\"C++\"", request.Body);
        Assert.Contains("\"tkSubId\":999", request.Body);
    }

    [Fact]
    public async Task AuthFailure_ThrowsAscAuthException()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":401,"msg":"请先登录"}"""));

        using var client = new AscClient(handler, "https://example.test/tiku");
        client.SetSession(new AscSession
        {
            BaseUrl = "https://example.test/tiku",
            Satoken = "expired"
        });

        await Assert.ThrowsAsync<AscAuthException>(() => client.ProbeSessionAsync());
    }

    [Fact]
    public async Task ApiFailure_ContainsMessageAndCode()
    {
        var handler = new FakeHttpMessageHandler(
            Json("""{"code":500,"msg":"boom","data":null}"""));

        using var client = new AscClient(handler, "https://example.test/tiku");
        client.SetSession(new AscSession
        {
            BaseUrl = "https://example.test/tiku",
            Satoken = "tok"
        });

        var ex = await Assert.ThrowsAsync<AscApiException>(() => client.ProbeSessionAsync());
        Assert.Equal(500, ex.Code);
        Assert.Contains("boom", ex.Message);
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private readonly List<CapturedRequest> _requests;

        public FakeHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
            _requests = new List<CapturedRequest>();
        }

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }
            }

            _requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                body,
                headers));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected HTTP request: " + request.RequestUri);
            }

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string Body,
        IReadOnlyDictionary<string, string> Headers)
    {
        public string Header(string name)
        {
            return Headers.TryGetValue(name, out var value) ? value : string.Empty;
        }
    }
}
