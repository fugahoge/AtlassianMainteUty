using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// Confluence のスペースにロールベースのアクセス権（スペースロール）を割り当てる
/// </summary>
internal static class SetSpaceRoles
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("SetSpaceRoles.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      if (args.Length < 1)
      {
        _logger.LogError("使用法: SetSpaceRoles <JSONファイル>");
        Console.WriteLine("使用法: SetSpaceRoles <JSONファイル>");
        return 1;
      }

      var jsonPath = args[0].Trim();
      var request = await ReadJsonFile(jsonPath);
      if (request == null)
      {
        return 1;
      }

      return await execSetSpaceRoles(request);
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "エラーが発生しました");
      Console.WriteLine($"エラー: {ex.Message}");
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
  private static async Task<SpaceRoleRequest?> ReadJsonFile(string filePath)
  {
    SpaceRoleRequest? request;

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

      request = JsonSerializer.Deserialize<SpaceRoleRequest>(json, options);
    }
    catch (JsonException ex)
    {
      _logger?.LogError(ex, "json のパースに失敗しました。");
      throw new JsonException("json parse failed", ex);
    }

    if (request?.Spaces == null || request.Spaces.Count == 0)
    {
      _logger?.LogError("json にスペースが含まれていません。");
      throw new InvalidOperationException("json contains no spaces");
    }

    // JSON の format と version をチェック
    if (request.Format != "space-role-request")
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
  /// JSON の内容に従ってスペースロールを割り当てる
  /// </summary>
  private static async Task<int> execSetSpaceRoles(SpaceRoleRequest request)
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    // ロールが使えないサイトでは何もできないため、先にモードを確認する
    var roleMode = await CommonConfluence.GetSpaceRoleModeAsync(httpClient, config);
    logger.LogInformation("スペースアクセスモード: {RoleMode}", string.IsNullOrEmpty(roleMode) ? "(取得できませんでした)" : roleMode);

    // ロール名 -> ロール ID の解決用
    var roles = await CommonConfluence.GetSpaceRolesAsync(httpClient, config);
    if (roles.Count == 0)
    {
      logger.LogError("スペースロールの定義を取得できませんでした。サイトでロールベースアクセスが有効か確認してください。");
      return 1;
    }
    var roleIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var role in roles)
    {
      if (!string.IsNullOrEmpty(role.Name))
        roleIdByName[role.Name] = role.Id;
    }

    // スペースキー -> スペース ID の解決用
    var spaceIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var space in await CommonConfluence.GetAllSpacesAsync(httpClient, config))
    {
      spaceIdByKey[space.Key] = space.Id;
    }

    foreach (var space in request.Spaces)
    {
      var spaceId = await ResolveSpaceIdAsync(space, spaceIdByKey);
      if (string.IsNullOrEmpty(spaceId))
        return 1;

      var spaceLabel = string.IsNullOrWhiteSpace(space.SpaceKey) ? spaceId : space.SpaceKey;

      foreach (var assignment in space.Assignments ?? Array.Empty<SpaceRoleAssignmentRequest>())
      {
        var result = await execAssignRole(httpClient, config, spaceId, spaceLabel, assignment, roleIdByName);
        if (result != 0)
          return 1;
      }
    }

    return 0;
  }

  /// <summary>
  /// spaceId、または spaceKey からスペース ID を解決する
  /// </summary>
  private static Task<string> ResolveSpaceIdAsync(SpaceRoleRequestSpace space, Dictionary<string, string> spaceIdByKey)
  {
    var logger = _logger!;

    if (!string.IsNullOrWhiteSpace(space.SpaceId))
      return Task.FromResult(space.SpaceId.Trim());

    if (string.IsNullOrWhiteSpace(space.SpaceKey))
    {
      logger.LogError("spaceId と spaceKey のどちらも指定されていません。");
      return Task.FromResult("");
    }

    if (spaceIdByKey.TryGetValue(space.SpaceKey.Trim(), out var spaceId))
      return Task.FromResult(spaceId);

    logger.LogError("スペースが見つかりません: {SpaceKey}", space.SpaceKey);
    return Task.FromResult("");
  }

  /// <summary>
  /// ロール割当を 1 件実行する
  /// </summary>
  private static async Task<int> execAssignRole(
    HttpClient httpClient,
    AtlassianConfig config,
    string spaceId,
    string spaceLabel,
    SpaceRoleAssignmentRequest assignment,
    Dictionary<string, string> roleIdByName)
  {
    var logger = _logger!;

    var principalType = (assignment.PrincipalType ?? "").Trim();
    if (string.IsNullOrEmpty(principalType))
    {
      logger.LogError("principalType が指定されていません。(スペース: {SpaceLabel})", spaceLabel);
      return 1;
    }

    var principalId = await ResolvePrincipalIdAsync(httpClient, config, principalType, assignment);
    if (string.IsNullOrEmpty(principalId))
      return 1;

    var roleId = ResolveRoleId(assignment, roleIdByName);
    if (string.IsNullOrEmpty(roleId))
      return 1;

    var principalLabel = FirstNotEmpty(assignment.Mail, assignment.GroupName, principalId);
    var roleLabel = FirstNotEmpty(assignment.RoleName, roleId);

    logger.LogInformation("ロール割当：{SpaceLabel} / {PrincipalType} {PrincipalLabel} -> {RoleLabel}",
      spaceLabel, principalType, principalLabel, roleLabel);

    (var response, var rawResponse) = await CommonConfluence.AssignSpaceRoleAsync(
      httpClient, config, spaceId, roleId, principalId, principalType);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        logger.LogError("ロール割当に失敗しました (HTTP {StatusCode})", (int)response.StatusCode);
        if (!string.IsNullOrEmpty(rawResponse))
          logger.LogError("{Response}", rawResponse);
        return 1;
      }
    }

    return 0;
  }

  /// <summary>
  /// principalId、または mail / groupName からプリンシパル ID を解決する
  /// </summary>
  private static async Task<string> ResolvePrincipalIdAsync(
    HttpClient httpClient, AtlassianConfig config, string principalType, SpaceRoleAssignmentRequest assignment)
  {
    var logger = _logger!;

    if (!string.IsNullOrWhiteSpace(assignment.PrincipalId))
      return assignment.PrincipalId.Trim();

    if (!string.IsNullOrWhiteSpace(assignment.Mail))
    {
      var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config, assignment.Mail.Trim());
      if (string.IsNullOrEmpty(accountId))
      {
        logger.LogError("ユーザーが見つかりません: {Mail}", assignment.Mail);
        return "";
      }
      return accountId;
    }

    if (!string.IsNullOrWhiteSpace(assignment.GroupName))
    {
      var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, config, assignment.GroupName.Trim());
      if (string.IsNullOrEmpty(groupId))
      {
        logger.LogError("グループが見つかりません: {GroupName}", assignment.GroupName);
        return "";
      }
      return groupId;
    }

    logger.LogError("principalId / mail / groupName のいずれも指定されていません。(principalType: {PrincipalType})", principalType);
    return "";
  }

  /// <summary>
  /// roleId、または roleName からロール ID を解決する
  /// </summary>
  private static string ResolveRoleId(SpaceRoleAssignmentRequest assignment, Dictionary<string, string> roleIdByName)
  {
    var logger = _logger!;

    if (!string.IsNullOrWhiteSpace(assignment.RoleId))
      return assignment.RoleId.Trim();

    if (string.IsNullOrWhiteSpace(assignment.RoleName))
    {
      logger.LogError("roleId と roleName のどちらも指定されていません。");
      return "";
    }

    if (roleIdByName.TryGetValue(assignment.RoleName.Trim(), out var roleId))
      return roleId;

    logger.LogError("ロールが見つかりません: {RoleName}", assignment.RoleName);
    return "";
  }

  /// <summary>
  /// 最初に空でない文字列を返す
  /// </summary>
  private static string FirstNotEmpty(params string?[] values)
  {
    foreach (var value in values)
    {
      if (!string.IsNullOrWhiteSpace(value))
        return value.Trim();
    }
    return "";
  }
}
