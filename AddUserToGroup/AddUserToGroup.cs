using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// グループにユーザーを追加する
/// </summary>
internal static class AddUserToGroup
{
  private static Config? _config;
  private static ILogger? _logger;

  private static readonly Hashtable _accountIdCache = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Hashtable _groupIdCache = new(StringComparer.OrdinalIgnoreCase);

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("AddUserToGroup.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      if (!File.Exists("input.json"))
      {
        _logger.LogError("input.json が見つかりません。");
        return 1;
      }

      var json = await File.ReadAllTextAsync("input.json");
      UserGroupRequest? request;
      try
      {
        var options = new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true,
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        request = JsonSerializer.Deserialize<UserGroupRequest>(json, options);
      }
      catch (JsonException ex)
      {
        _logger.LogError(ex, "json のパースに失敗しました。");
        return 1;
      }

      if (request?.Users == null || request.Users.Count == 0)
      {
        _logger.LogError("json にユーザーが含まれていません。");
        return 1;
      }

      var exitCode = 0;
      foreach (var user in request.Users)
      {
        if (string.IsNullOrEmpty(user.Mail))
        {
          _logger.LogWarning("メールアドレスが空のユーザーをスキップしました。");
          continue;
        }
        foreach (var groupName in user.AddGroup)
        {
          if (string.IsNullOrWhiteSpace(groupName))
            continue;
          var result = await execAddUserToGroup(user.Mail, groupName.Trim());
          if (result != 0)
            exitCode = 1;
        }
      }
      return exitCode;
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "エラーが発生しました");
      return 1;
    }
    finally
    {
      Serilog.Log.CloseAndFlush();
    }
  }

  /// <summary>
  /// グループにユーザーを追加する
  /// </summary>
  private static async Task<int> execAddUserToGroup(string email, string groupName)
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var accountId = await GetAccountIdByEmailAsync(httpClient, config, email);
    if (string.IsNullOrEmpty(accountId))
      return 1;

    var groupId = await GetGroupIdByNameAsync(httpClient, config, groupName);
    if (string.IsNullOrEmpty(groupId))
      return 1;

    logger.LogInformation("グループ追加...");
    (var response, var rawResponse) = await CommonAdmin.AddUserToGroupAsync(httpClient, config, accountId, groupId);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        if (IsAlreadyMemberError(response.StatusCode, rawResponse))
        {
          logger.LogInformation("ユーザーは指定されたグループに既に所属しています。");
          logger.LogInformation("ユーザー: {Email}, グループ: {GroupName}", email, groupName);
          return 0;
        }
        logger.LogError("グループ追加に失敗しました (HTTP {StatusCode})", (int)response.StatusCode);

        if (!string.IsNullOrEmpty(rawResponse))
          logger.LogError("API レスポンス: {Response}", rawResponse);
        return 1;
      }
      logger.LogInformation("処理が正常に完了しました。");
    }

    return 0;
  }

  /// <summary>
  /// メールアドレスから accountId を取得する。
  /// </summary>
  private static async Task<string?> GetAccountIdByEmailAsync(HttpClient httpClient, AtlassianConfig config, string email)
  {
    if (_accountIdCache[email] is string cached)
    {
      _logger?.LogInformation("accountId (キャッシュ): {AccountId}", cached);
      return cached;
    }
    var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config, email);
    if (string.IsNullOrEmpty(accountId))
    {
      _logger?.LogError("ユーザーが見つかりません (メール: {Email})", email);
      return null;
    }
    _accountIdCache[email] = accountId;
    _logger?.LogInformation("accountId: {AccountId}", accountId);
    return accountId;
  }

  /// <summary>
  /// グループ名から groupId を取得する。
  /// </summary>
  private static async Task<string?> GetGroupIdByNameAsync(HttpClient httpClient, AtlassianConfig config, string groupName)
  {
    if (_groupIdCache[groupName] is string cached)
    {
      _logger?.LogInformation("groupId (キャッシュ): {GroupId}", cached);
      return cached;
    }
    var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, config, groupName);
    if (string.IsNullOrEmpty(groupId))
    {
      _logger?.LogError("グループが見つかりません (名前: {GroupName})", groupName);
      return null;
    }
    _groupIdCache[groupName] = groupId;
    _logger?.LogInformation("groupId: {GroupId}", groupId);
    return groupId;
  }

  /// <summary>
  /// レスポンスが「既にグループに所属している」を示すか判定する。
  /// </summary>
  private static bool IsAlreadyMemberError(System.Net.HttpStatusCode statusCode, string? rawResponse)
  {
    // Atlassian Cloud Admin API の仕様では、「既に所属済み」と「その他の登録失敗」を区別できない
    // 「既に所属済み」の場合も、その他のバリデーションエラーと同様に 400 Bad Request が返る
    // メッセージに "Cannot add user. User is already a member of" のような文言が含まれることで判定する
    if (statusCode != System.Net.HttpStatusCode.BadRequest)
      return false;
    if (string.IsNullOrEmpty(rawResponse))
      return false;
    var lower = rawResponse.ToLowerInvariant();

    return lower.Contains("already a member");
  }
}
