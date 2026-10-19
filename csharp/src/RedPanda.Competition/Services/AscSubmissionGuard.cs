using System.Security.Cryptography;
using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscSubmissionGuard
{
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(90);

    public static string HashCode(string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }

    /// <summary>
    /// /api/submission returns a request identifier (often a GUID) that is
    /// accepted by the polling endpoint, while the compileInfo page is keyed by
    /// the numeric row id from the polling response. Prefer that row id for
    /// details/history and keep the request id only for polling.
    /// </summary>
    public static string ResolveDetailId(AscSubmissionResult result, string requestId)
    {
        var rowId = result.Id?.Trim() ?? string.Empty;
        if (rowId.Length > 0)
        {
            return rowId;
        }

        return requestId?.Trim() ?? string.Empty;
    }

    public static bool IsRecentDuplicate(
        AscProblemLink link,
        string codeHash,
        DateTime nowUtc,
        TimeSpan? window = null)
    {
        if (string.IsNullOrWhiteSpace(codeHash) ||
            string.IsNullOrWhiteSpace(link.LastSubmittedCodeHash))
        {
            return false;
        }

        window ??= DuplicateWindow;
        return link.LastSubmittedAtUtc > nowUtc - window &&
               string.Equals(link.LastSubmittedCodeHash, codeHash, StringComparison.Ordinal);
    }
}
