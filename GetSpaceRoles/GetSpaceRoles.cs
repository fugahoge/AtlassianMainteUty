using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// Confluence のロール定義と、全スペースのロール割当（新・ロールベースアクセス）を取得する
/// </summary>
internal static class GetSpaceRoles
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("GetSpaceRoles.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      var outputPath = args.Length >= 1 ? args[0].Trim() : "space-roles.json";

      var json = await execGetSpaceRoles();
      await File.WriteAllTextAsync(outputPath, json);

      _logger.LogInformation("出力しました: {OutputPath}", outputPath);
      return 0;
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
  /// ロール定義とロール割当を space-role-list 形式の JSON で返す
  /// </summary>
  private static async Task<string> execGetSpaceRoles()
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    // サイトのスペースアクセスモード（pre-roles / roles transition / roles only）
    var roleMode = await CommonConfluence.GetSpaceRoleModeAsync(httpClient, config);
    logger.LogInformation("スペースアクセスモード: {RoleMode}", string.IsNullOrEmpty(roleMode) ? "(取得できませんでした)" : roleMode);

    // サイトで利用可能なロールの一覧（space-role-request の roleName / roleId はここから選ぶ）
    var roles = await CommonConfluence.GetSpaceRolesAsync(httpClient, config);
    logger.LogInformation("ロール定義を {Count} 件取得しました", roles.Count);

    var roleNameById = roles.ToDictionary(r => r.Id, r => r.Name, StringComparer.OrdinalIgnoreCase);
    var roleCatalog = roles
      .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
      .Select(r => new SpaceRoleCatalogItem(r.Id, r.Name, r.Type, r.Description, r.SpacePermissions))
      .ToList();

    var spaces = await CommonConfluence.GetAllSpacesAsync(httpClient, config);
    logger.LogInformation("スペースを {Count} 件取得しました", spaces.Count);

    var spaceItems = new List<SpaceRoleListSpace>();

    foreach (var space in spaces.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
    {
      logger.LogInformation("ロール割当取得：{SpaceKey} ({SpaceName})", space.Key, space.Name);

      var assignments = await CommonConfluence.GetRoleAssignmentsAsync(httpClient, config, space.Id);

      var items = new List<SpaceRoleListAssignment>();
      foreach (var assignment in assignments
        .OrderBy(a => a.PrincipalType, StringComparer.OrdinalIgnoreCase)
        .ThenBy(a => a.PrincipalId, StringComparer.OrdinalIgnoreCase))
      {
        var name = await CommonConfluence.ResolvePrincipalNameAsync(
          httpClient, config, assignment.PrincipalType, assignment.PrincipalId);

        items.Add(new SpaceRoleListAssignment(
          assignment.PrincipalType,
          assignment.PrincipalId,
          name,
          assignment.RoleId,
          roleNameById.TryGetValue(assignment.RoleId, out var roleName) ? roleName : ""));
      }

      spaceItems.Add(new SpaceRoleListSpace(space.Id, space.Key, space.Name, items));
    }

    var output = new SpaceRoleList(
      Format: "space-role-list",
      Version: "1.0",
      CreateDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
      RoleMode: roleMode ?? "",
      Roles: roleCatalog,
      Spaces: spaceItems);

    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    return JsonSerializer.Serialize(output, options);
  }
}
