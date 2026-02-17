using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// ユーザーのグループを設定する（追加・削除）
/// </summary>
internal static class SetUserToGroup
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("SetUserToGroup.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      // JSON ファイルを読み込む
      var request = await ReadJsonFile("input.json");
      if (request == null)
      {
        return 1;
      }

      // ユーザーをグループに追加・削除
      foreach (var user in request.Users)
      {
        if (string.IsNullOrEmpty(user.Mail))
        {
          continue;
        }

        // deleteAll が true の場合は、すべてのグループを削除
        if (user.DeleteAll)
        {
          var result = await execRemoveUserFromAllGroups(user.Mail);
          if (result != 0)
            return 1;
        }
        else
        {
          // グループから削除
          foreach (var groupName in user.DelGroup)
          {
            if (string.IsNullOrWhiteSpace(groupName))
            {
              continue;
            }

            var result = await execRemoveUserFromGroup(user.Mail, groupName.Trim());
            if (result != 0)
            {
              return 1;
            }
          }
        }

        // グループを追加
        foreach (var groupName in user.AddGroup)
        {
          if (string.IsNullOrWhiteSpace(groupName))
          {
            continue;
          }

          var result = await execAddUserToGroup(user.Mail, groupName.Trim());
          if (result != 0)
          {
            return 1;
          }
        }
      }

      return 0;
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
  /// JSON ファイルを読み込む
  /// </summary>
  private static async Task<UserGroupRequest?> ReadJsonFile(string filePath)
  {
    UserGroupRequest? request;

    if (!File.Exists(filePath))
    {
      _logger?.LogError("{FilePath} が見つかりません。", filePath);
      throw new FileNotFoundException(filePath);
    }

    try
    {
      var json = await File.ReadAllTextAsync(filePath);
    
      var options = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };
      
      request = JsonSerializer.Deserialize<UserGroupRequest>(json, options);
    }
    catch (JsonException ex)
    {
      _logger?.LogError(ex, "json のパースに失敗しました。");
      throw new JsonException("json parse failed", ex);
    }

    if (request?.Users == null || request.Users.Count == 0)
    {
      _logger?.LogError("json にユーザーが含まれていません。");
      throw new InvalidOperationException("json contains no users");
    }

    // JSON の format と version をチェック
    if (request.Format != "user-group-request")
    {
      _logger?.LogError("json の format が不正です。");
      throw new InvalidOperationException("format is invalid");
    }
    if (request.Version != "1.0")
    {
      _logger?.LogError("json の version が不正です。");
      throw new InvalidOperationException("version is invalid");
    }

    return request;
  }

  /// <summary>
  /// グループにユーザーを追加する
  /// </summary>
  private static async Task<int> execAddUserToGroup(string email, string groupName)
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    // accountId を取得
    var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config, email);
    if (string.IsNullOrEmpty(accountId))
      return 1;

    // groupId を取得
    var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, config, groupName);
    if (string.IsNullOrEmpty(groupId))
      return 1;

    logger.LogInformation("グループ追加：{Email} -> {GroupName}", email, groupName);
    (var response, var rawResponse) = await CommonAdmin.AddUserToGroupAsync(httpClient, config, accountId, groupId);

    using (response)
    {
      // 処理に失敗した場合
      if (!response.IsSuccessStatusCode)
      {
        if (CommonJIRA.IsAlreadyMemberError(response.StatusCode, rawResponse))
        {
          logger.LogInformation("指定されたグループに既に所属しています。");
          return 0;
        }

        logger.LogError("グループ追加に失敗しました (HTTP {StatusCode})", (int)response.StatusCode);
        if (!string.IsNullOrEmpty(rawResponse))
          logger.LogError("{Response}", rawResponse);
        return 1;
      }
    }

    return 0;
  }

  /// <summary>
  /// 所属する全グループからユーザーを削除する
  /// </summary>
  private static async Task<int> execRemoveUserFromAllGroups(string email)
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config, email);
    if (string.IsNullOrEmpty(accountId))
      return 1;

    var groupNames = await CommonJIRA.GetGroupNamesByAccountIdAsync(httpClient, config, accountId);
    if (groupNames.Count == 0)
    {
      logger.LogInformation("ユーザーはどのグループにも所属していません: {Email}", email);
      return 0;
    }

    foreach (var groupName in groupNames)
    {
      var result = await execRemoveUserFromGroup(email, groupName);
      if (result != 0)
        return 1;
    }

    return 0;
  }

  /// <summary>
  /// グループからユーザーを削除する
  /// </summary>
  private static async Task<int> execRemoveUserFromGroup(string email, string groupName)
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config, email);
    if (string.IsNullOrEmpty(accountId))
      return 1;

    var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, config, groupName);
    if (string.IsNullOrEmpty(groupId))
      return 1;

    logger.LogInformation("グループ削除：{Email} <- {GroupName}", email, groupName);
    (var response, var rawResponse) = await CommonAdmin.RemoveUserFromGroupAsync(httpClient, config, accountId, groupId);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        logger.LogError("グループ削除に失敗しました (HTTP {StatusCode})", (int)response.StatusCode);
        if (!string.IsNullOrEmpty(rawResponse))
          logger.LogError("{Response}", rawResponse);
        return 1;
      }
    }

    return 0;
  }
}
