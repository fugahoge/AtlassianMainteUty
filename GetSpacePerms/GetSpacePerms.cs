using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// Confluence の全スペースについて、旧・粒度権限（個別権限）を取得する
/// </summary>
internal static class GetSpacePerms
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("GetSpacePerms.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      var outputPath = args.Length >= 1 ? args[0].Trim() : "space-permissions.json";

      var json = await execGetSpacePerms();
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
  /// スペースの個別権限を space-permission-list 形式の JSON で返す
  /// </summary>
  private static async Task<string> execGetSpacePerms()
  {
    var config = _config!.Atlassian;
    var logger = _logger!;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var spaces = await CommonConfluence.GetAllSpacesAsync(httpClient, config);
    logger.LogInformation("スペースを {Count} 件取得しました", spaces.Count);

    var spaceItems = new List<SpacePermissionSpace>();

    foreach (var space in spaces.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
    {
      logger.LogInformation("スペース権限取得：{SpaceKey} ({SpaceName})", space.Key, space.Name);

      var grants = await CommonConfluence.GetSpacePermissionsAsync(httpClient, config, space.Id);

      // プリンシパル単位に権限をまとめる
      var principals = new List<SpacePermissionPrincipal>();
      var grouped = grants
        .GroupBy(g => (g.PrincipalType, g.PrincipalId))
        .OrderBy(g => g.Key.PrincipalType, StringComparer.OrdinalIgnoreCase)
        .ThenBy(g => g.Key.PrincipalId, StringComparer.OrdinalIgnoreCase);

      foreach (var group in grouped)
      {
        var (principalType, principalId) = group.Key;
        var name = await CommonConfluence.ResolvePrincipalNameAsync(httpClient, config, principalType, principalId);

        principals.Add(new SpacePermissionPrincipal(
          principalType,
          principalId,
          name,
          group.Select(g => g.Operation).Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList()));
      }

      spaceItems.Add(new SpacePermissionSpace(space.Id, space.Key, space.Name, principals));
    }

    var output = new SpacePermissionList(
      Format: "space-permission-list",
      Version: "1.0",
      CreateDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
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
