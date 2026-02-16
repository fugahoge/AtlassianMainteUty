using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AtlassianMainteUty;

/// <summary>
/// Jira REST API の共通呼び出し
/// </summary>
public static class CommonJIRA
{
  private const int DefaultMaxRetries = 3;

  private static readonly Hashtable AccountIdCache = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Hashtable GroupIdCache = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>メールアドレスでユーザーを検索し accountId を取得</summary>
  public static async Task<string?> GetAccountIdByEmailAsync(HttpClient client, AtlassianConfig config, string email, CancellationToken cancellationToken = default)
  {
    if (AccountIdCache[email] is string cached)
    {
      return cached;
    }

    var accountId = await FetchAccountIdByEmailAsync(client, config, email, cancellationToken);
    if (string.IsNullOrEmpty(accountId))
    {
      return null;
    }

    AccountIdCache[email] = accountId;
    return accountId;
  }

  private static async Task<string?> FetchAccountIdByEmailAsync(HttpClient client, AtlassianConfig config, string email, CancellationToken cancellationToken)
  {
    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () =>
      {
        var url = $"{config.JiraBaseUrl.TrimEnd('/')}/rest/api/3/users/search?query={Uri.EscapeDataString(email)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = CreateBasicAuth(config.JiraAuthEmail, config.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
      },
      DefaultMaxRetries,
      cancellationToken);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
        return null;

      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        return null;

      var first = root[0];
      return first.TryGetProperty("accountId", out var idProp) ? idProp.GetString() : null;
    }
  }

  /// <summary>グループ名で groupuserpicker を検索し groupId を取得</summary>
  public static async Task<string?> GetGroupIdByNameAsync(HttpClient client, AtlassianConfig config, string groupName, CancellationToken cancellationToken = default)
  {
    if (GroupIdCache[groupName] is string cached)
    {
      return cached;
    }

    var groupId = await FetchGroupIdByNameAsync(client, config, groupName, cancellationToken);
    if (string.IsNullOrEmpty(groupId))
    {
      return null;
    }

    GroupIdCache[groupName] = groupId;
    return groupId;
  }

  private static async Task<string?> FetchGroupIdByNameAsync(HttpClient client, AtlassianConfig config, string groupName, CancellationToken cancellationToken)
  {
    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () =>
      {
        var url = $"{config.JiraBaseUrl.TrimEnd('/')}/rest/api/3/groupuserpicker?query={Uri.EscapeDataString(groupName)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = CreateBasicAuth(config.JiraAuthEmail, config.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
      },
      DefaultMaxRetries,
      cancellationToken);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
        return null;

      using var doc = JsonDocument.Parse(json);
      if (!doc.RootElement.TryGetProperty("groups", out var groupsNode) ||
        !groupsNode.TryGetProperty("groups", out var list))
        return null;

      foreach (var g in list.EnumerateArray())
      {
        if (g.TryGetProperty("name", out var nameProp) &&
          string.Equals(nameProp.GetString(), groupName, StringComparison.OrdinalIgnoreCase) &&
          g.TryGetProperty("groupId", out var idProp))
          return idProp.GetString();
      }
      return null;
    }
  }

  /// <summary>accountId で groups/picker を呼び出し、所属グループ名一覧を取得</summary>
  public static async Task<IReadOnlyList<string>> GetGroupNamesByAccountIdAsync(HttpClient client, AtlassianConfig config, string accountId, CancellationToken cancellationToken = default)
  {
    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () =>
      {
        var url = $"{config.JiraBaseUrl.TrimEnd('/')}/rest/api/3/groups/picker?accountId={Uri.EscapeDataString(accountId)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = CreateBasicAuth(config.JiraAuthEmail, config.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
      },
      DefaultMaxRetries,
      cancellationToken);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
        return Array.Empty<string>();

      try
      {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("groups", out var groupsNode))
          return Array.Empty<string>();

        JsonElement arrayElement;
        if (groupsNode.ValueKind == JsonValueKind.Array)
          arrayElement = groupsNode;
        else if (groupsNode.TryGetProperty("groups", out var inner))
          arrayElement = inner;
        else
          return Array.Empty<string>();

        var list = new List<string>();
        foreach (var g in arrayElement.EnumerateArray())
        {
          if (g.TryGetProperty("name", out var nameProp))
          {
            var name = nameProp.GetString();
            if (!string.IsNullOrEmpty(name))
              list.Add(name);
          }
        }
        return list;
      }
      catch (JsonException)
      {
        return Array.Empty<string>();
      }
    }
  }

  /// <summary>「既にグループに所属している」を判定する</summary>
  public static bool IsAlreadyMemberError(HttpStatusCode statusCode, string? rawResponse)
  {
    if (statusCode != HttpStatusCode.BadRequest)
      return false;
    if (string.IsNullOrEmpty(rawResponse))
      return false;
    var lower = rawResponse.ToLowerInvariant();
    return lower.Contains("already a member");
  }

  /// <summary>Basic 認証用の Authorization ヘッダ値を生成する</summary>
  private static AuthenticationHeaderValue CreateBasicAuth(string email, string token)
  {
    var credential = $"{email}:{token}";
    var bytes = Encoding.UTF8.GetBytes(credential);
    var encoded = Convert.ToBase64String(bytes);
    return new AuthenticationHeaderValue("Basic", encoded);
  }
}
