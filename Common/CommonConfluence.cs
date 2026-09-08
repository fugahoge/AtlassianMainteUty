using System.Collections;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AtlassianMainteUty;

/// <summary>
/// Confluence Cloud REST API の共通呼び出し
/// スペース権限（旧・粒度権限）とスペースロール（RBAC）を扱う
/// </summary>
public static class CommonConfluence
{
  private const int DefaultMaxRetries = 3;
  private const int DefaultPageLimit = 100;

  private static readonly Hashtable GroupNameCache = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Hashtable UserNameCache = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Confluence のベース URL を解決する
  /// ConfluenceBaseUrl が未設定の場合は JiraBaseUrl + /wiki を使う
  /// </summary>
  public static string ResolveBaseUrl(AtlassianConfig config)
  {
    if (!string.IsNullOrWhiteSpace(config.ConfluenceBaseUrl))
      return config.ConfluenceBaseUrl.TrimEnd('/');

    return config.JiraBaseUrl.TrimEnd('/') + "/wiki";
  }

  /// <summary>スペース一覧を取得</summary>
  public static async Task<List<ConfluenceSpace>> GetAllSpacesAsync(
    HttpClient client, AtlassianConfig config, CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);
    var results = new List<ConfluenceSpace>();

    await ForEachPageAsync(client, config, item =>
    {
      var id = GetString(item, "id");
      var key = GetString(item, "key");
      if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key))
        return;

      results.Add(new ConfluenceSpace(
        id,
        key,
        GetString(item, "name"),
        GetString(item, "type"),
        GetString(item, "status")));
    },
    cursor =>
    {
      var url = $"{baseUrl}/api/v2/spaces?limit={DefaultPageLimit}";
      if (!string.IsNullOrEmpty(cursor))
        url += "&cursor=" + Uri.EscapeDataString(cursor);
      return url;
    },
    cancellationToken);

    return results;
  }

  /// <summary>スペースの個別権限（旧・粒度権限）を取得</summary>
  public static async Task<List<SpacePermissionGrant>> GetSpacePermissionsAsync(
    HttpClient client, AtlassianConfig config, string spaceId, CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);
    var results = new List<SpacePermissionGrant>();

    await ForEachPageAsync(client, config, item =>
    {
      var principalType = "";
      var principalId = "";
      if (item.TryGetProperty("principal", out var principal))
      {
        principalType = GetString(principal, "type");
        principalId = GetString(principal, "id");
      }

      var operation = "";
      if (item.TryGetProperty("operation", out var op))
      {
        var key = GetString(op, "key");
        var targetType = GetString(op, "targetType");
        operation = string.IsNullOrEmpty(targetType) ? key : $"{key}:{targetType}";
      }

      if (string.IsNullOrEmpty(operation))
        return;

      results.Add(new SpacePermissionGrant(GetString(item, "id"), principalType, principalId, operation));
    },
    cursor =>
    {
      var url = $"{baseUrl}/api/v2/spaces/{Uri.EscapeDataString(spaceId)}/permissions?limit={DefaultPageLimit}";
      if (!string.IsNullOrEmpty(cursor))
        url += "&cursor=" + Uri.EscapeDataString(cursor);
      return url;
    },
    cancellationToken);

    return results;
  }

  /// <summary>サイトで利用可能なスペースロールの定義を取得</summary>
  public static async Task<List<SpaceRoleDefinition>> GetSpaceRolesAsync(
    HttpClient client, AtlassianConfig config, CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);
    var results = new List<SpaceRoleDefinition>();

    await ForEachPageAsync(client, config, item =>
    {
      var id = GetString(item, "id");
      if (string.IsNullOrEmpty(id))
        return;

      var permissions = new List<string>();
      if (item.TryGetProperty("spacePermissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
      {
        foreach (var p in perms.EnumerateArray())
        {
          // 文字列の配列だが、オブジェクトで返る場合に備えて id / displayName も拾う
          var value = p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : (GetString(p, "id") is { Length: > 0 } pid ? pid : GetString(p, "displayName"));
          if (!string.IsNullOrEmpty(value))
            permissions.Add(value);
        }
      }

      results.Add(new SpaceRoleDefinition(
        id,
        GetString(item, "type"),
        GetString(item, "name"),
        GetString(item, "description"),
        permissions));
    },
    cursor =>
    {
      var url = $"{baseUrl}/api/v2/space-roles?limit={DefaultPageLimit}";
      if (!string.IsNullOrEmpty(cursor))
        url += "&cursor=" + Uri.EscapeDataString(cursor);
      return url;
    },
    cancellationToken);

    return results;
  }

  /// <summary>スペースのロール割当を取得</summary>
  public static async Task<List<SpaceRoleAssignment>> GetRoleAssignmentsAsync(
    HttpClient client, AtlassianConfig config, string spaceId, CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);
    var results = new List<SpaceRoleAssignment>();

    await ForEachPageAsync(client, config, item =>
    {
      var roleId = GetString(item, "roleId");
      var principalId = GetString(item, "principalId");
      if (string.IsNullOrEmpty(roleId) && string.IsNullOrEmpty(principalId))
        return;

      results.Add(new SpaceRoleAssignment(roleId, principalId, GetString(item, "principalType")));
    },
    cursor =>
    {
      var url = $"{baseUrl}/api/v2/spaces/{Uri.EscapeDataString(spaceId)}/role-assignments?limit={DefaultPageLimit}";
      if (!string.IsNullOrEmpty(cursor))
        url += "&cursor=" + Uri.EscapeDataString(cursor);
      return url;
    },
    cancellationToken);

    return results;
  }

  /// <summary>スペースアクセスのモード（pre-roles / roles transition / roles only）を取得</summary>
  public static async Task<string?> GetSpaceRoleModeAsync(
    HttpClient client, AtlassianConfig config, CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);

    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () => CreateGetRequest($"{baseUrl}/api/v2/space-role-mode", config),
      DefaultMaxRetries,
      cancellationToken);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
        return null;

      try
      {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
          return root.GetString();

        var mode = GetString(root, "mode");
        return string.IsNullOrEmpty(mode) ? GetString(root, "spaceRoleMode") : mode;
      }
      catch (JsonException)
      {
        return null;
      }
    }
  }

  /// <summary>スペースにロールを割り当てる</summary>
  public static async Task<(HttpResponseMessage Response, string? RawBody)> AssignSpaceRoleAsync(
    HttpClient client, AtlassianConfig config, string spaceId, string roleId, string principalId, string principalType,
    CancellationToken cancellationToken = default)
  {
    var baseUrl = ResolveBaseUrl(config);

    (var response, var body) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () =>
      {
        var url = $"{baseUrl}/api/v2/spaces/{Uri.EscapeDataString(spaceId)}/role-assignments";
        var reqBody = new { roleId, principalId, principalType };
        var content = new StringContent(JsonSerializer.Serialize(reqBody), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = CommonHttp.CreateBasicAuth(config.JiraAuthEmail, config.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
      },
      DefaultMaxRetries,
      cancellationToken);

    return (response, body);
  }

  /// <summary>グループ ID からグループ名を取得（取得できない場合は空文字）</summary>
  public static async Task<string> GetGroupNameByIdAsync(
    HttpClient client, AtlassianConfig config, string groupId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrEmpty(groupId))
      return "";
    if (GroupNameCache[groupId] is string cached)
      return cached;

    var baseUrl = ResolveBaseUrl(config);

    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () => CreateGetRequest($"{baseUrl}/rest/api/group/by-id?id={Uri.EscapeDataString(groupId)}", config),
      DefaultMaxRetries,
      cancellationToken);

    var name = "";
    using (response)
    {
      if (response.IsSuccessStatusCode)
      {
        try
        {
          using var doc = JsonDocument.Parse(json);
          name = GetString(doc.RootElement, "name");
        }
        catch (JsonException)
        {
          name = "";
        }
      }
    }

    GroupNameCache[groupId] = name;
    return name;
  }

  /// <summary>accountId からユーザーの表示名（メールアドレス優先）を取得（取得できない場合は空文字）</summary>
  public static async Task<string> GetUserNameByAccountIdAsync(
    HttpClient client, AtlassianConfig config, string accountId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrEmpty(accountId))
      return "";
    if (UserNameCache[accountId] is string cached)
      return cached;

    var url = $"{config.JiraBaseUrl.TrimEnd('/')}/rest/api/3/user?accountId={Uri.EscapeDataString(accountId)}";

    (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
      client,
      () => CreateGetRequest(url, config),
      DefaultMaxRetries,
      cancellationToken);

    var name = "";
    using (response)
    {
      if (response.IsSuccessStatusCode)
      {
        try
        {
          using var doc = JsonDocument.Parse(json);
          var email = GetString(doc.RootElement, "emailAddress");
          name = string.IsNullOrEmpty(email) ? GetString(doc.RootElement, "displayName") : email;
        }
        catch (JsonException)
        {
          name = "";
        }
      }
    }

    UserNameCache[accountId] = name;
    return name;
  }

  /// <summary>
  /// プリンシパルの表示名を解決する
  /// user はメールアドレス、group はグループ名。解決できない種別は id をそのまま返す
  /// </summary>
  public static async Task<string> ResolvePrincipalNameAsync(
    HttpClient client, AtlassianConfig config, string principalType, string principalId,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrEmpty(principalId))
      return "";

    if (principalType.Equals("user", StringComparison.OrdinalIgnoreCase))
      return await GetUserNameByAccountIdAsync(client, config, principalId, cancellationToken);

    if (principalType.Equals("group", StringComparison.OrdinalIgnoreCase))
      return await GetGroupNameByIdAsync(client, config, principalId, cancellationToken);

    // アクセスクラスなど、名前を引けない種別は id をそのまま名前として扱う
    return principalId;
  }

  /// <summary>
  /// カーソルページングを繰り返し、results の各要素を handleItem に渡す
  /// </summary>
  private static async Task ForEachPageAsync(
    HttpClient client,
    AtlassianConfig config,
    Action<JsonElement> handleItem,
    Func<string?, string> buildUrl,
    CancellationToken cancellationToken)
  {
    string? cursor = null;

    do
    {
      (var response, var json) = await CommonHttp.ExecuteWithRetryAsync(
        client,
        () => CreateGetRequest(buildUrl(cursor), config),
        DefaultMaxRetries,
        cancellationToken);

      using (response)
      {
        if (!response.IsSuccessStatusCode)
          throw new HttpRequestException(BuildErrorMessage(response, json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in items.EnumerateArray())
            handleItem(item);
        }

        cursor = ExtractNextCursor(root);
      }
    } while (!string.IsNullOrEmpty(cursor));
  }

  /// <summary>
  /// API 失敗時のメッセージを組み立てる（本文が HTML の場合もあるため長さを制限する）
  /// </summary>
  private static string BuildErrorMessage(HttpResponseMessage response, string body)
  {
    var statusCode = (int)response.StatusCode;
    var hint = statusCode switch
    {
      401 => " 認証に失敗しました。Config.json の JiraAuthEmail / ApiToken を確認してください。",
      403 => " 権限が不足しています。Confluence の管理者権限があるアカウントか確認してください。",
      404 => " エンドポイントが見つかりません。ConfluenceBaseUrl とサイトのロール有効化状況を確認してください。",
      _ => ""
    };

    var detail = (body ?? "").Trim();
    if (detail.StartsWith('<'))
      detail = "(HTML レスポンス)";
    else if (detail.Length > 500)
      detail = detail[..500] + "...";

    return $"Confluence API の呼び出しに失敗しました (HTTP {statusCode}).{hint} {detail}".TrimEnd();
  }

  /// <summary>_links.next から次ページの cursor を取り出す</summary>
  private static string? ExtractNextCursor(JsonElement root)
  {
    if (!root.TryGetProperty("_links", out var links))
      return null;
    if (!links.TryGetProperty("next", out var next))
      return null;

    var nextVal = next.GetString();
    if (string.IsNullOrEmpty(nextVal))
      return null;

    var queryIndex = nextVal.IndexOf('?');
    if (queryIndex < 0)
      return null;

    foreach (var part in nextVal[(queryIndex + 1)..].Split('&'))
    {
      var separator = part.IndexOf('=');
      if (separator > 0 && part[..separator] == "cursor")
        return Uri.UnescapeDataString(part[(separator + 1)..]);
    }

    return null;
  }

  /// <summary>Basic 認証付きの GET リクエストを生成する</summary>
  private static HttpRequestMessage CreateGetRequest(string url, AtlassianConfig config)
  {
    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = CommonHttp.CreateBasicAuth(config.JiraAuthEmail, config.ApiToken);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return request;
  }

  /// <summary>JSON の文字列プロパティを取得する（無い場合は空文字）</summary>
  private static string GetString(JsonElement element, string propertyName)
  {
    if (element.ValueKind != JsonValueKind.Object)
      return "";
    if (!element.TryGetProperty(propertyName, out var value))
      return "";

    return value.ValueKind switch
    {
      JsonValueKind.String => value.GetString() ?? "",
      JsonValueKind.Number => value.ToString(),
      _ => ""
    };
  }
}
