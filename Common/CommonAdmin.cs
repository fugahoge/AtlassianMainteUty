using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AtlassianMainteUty;

/// <summary>
/// Atlassian Admin REST API の共通呼び出し（HTTP 送信・リトライは CommonHttp に委譲）
/// </summary>
public static class CommonAdmin
{
  private const int DefaultMaxRetries = 3;

  /// <summary>グループにユーザーを追加（失敗時は最大 DefaultMaxRetries 回までリトライ）</summary>
  public static async Task<(HttpResponseMessage Response, string? RawBody)> AddUserToGroupAsync(
    HttpClient client, string accountId, string groupId, CancellationToken cancellationToken = default)
  {
    (var response, var body) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () =>
      {
        var url = $"{Common.AdminApiBaseUrl.TrimEnd('/')}/admin/v1/orgs/{Common.OrgId}/directory/groups/{groupId}/memberships";
        var reqBody = new { account_id = accountId };
        var content = new StringContent(JsonSerializer.Serialize(reqBody), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Common.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
      },
      DefaultMaxRetries,
      cancellationToken);

    return (response, body);
  }

  /// <summary>組織の全ユーザーを取得（cursor ページング。各リクエストでリトライあり）</summary>
  public static async Task<List<(string AccountId, string? Email, string? DisplayName)>> GetAllOrgUsersAsync(HttpClient client, CancellationToken cancellationToken = default)
  {
    var results = new List<(string, string?, string?)>();
    string? cursor = null;

    do
    {
      (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
        client,
        () =>
        {
          var url = $"{Common.AdminApiBaseUrl.TrimEnd('/')}/admin/v1/orgs/{Common.OrgId}/users";
          if (!string.IsNullOrEmpty(cursor))
            url += "?cursor=" + Uri.EscapeDataString(cursor);
          var request = new HttpRequestMessage(HttpMethod.Get, url);
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Common.ApiKey);
          request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
          return request;
        },
        DefaultMaxRetries,
        cancellationToken);

      using (response)
      {
        if (!response.IsSuccessStatusCode)
          break;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
          foreach (var user in data.EnumerateArray())
          {
            var aid = user.TryGetProperty("account_id", out var a) ? a.GetString() ?? ""
              : (user.TryGetProperty("accountId", out var a2) ? a2.GetString() ?? "" : "");
            var email = user.TryGetProperty("email", out var em) ? em.GetString() : null;
            var displayName = user.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            if (!string.IsNullOrEmpty(aid))
              results.Add((aid, email, displayName));
          }
        }

        cursor = null;
        if (root.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var next))
        {
          var nextVal = next.GetString();
          if (!string.IsNullOrEmpty(nextVal))
            cursor = nextVal;
        }
      }
    } while (!string.IsNullOrEmpty(cursor));

    return results;
  }
}
