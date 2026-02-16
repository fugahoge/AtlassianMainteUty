using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// テナント全ユーザーの所属グループを表示する
/// </summary>
internal static class GetUserGroups
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("GetUserGroups.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      var json = await execGetUserGroup();
      await File.WriteAllTextAsync("output.json", json);

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
  /// テナント全ユーザーの所属グループを JSON 形式で返す
  /// </summary>
  private static async Task<string> execGetUserGroup()
  {
    var config = _config!.Atlassian;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var users = await CommonAdmin.GetAllOrgUsersAsync(httpClient, config);
    var userItems = new List<UserGroup>();

    if (users.Count != 0)
    {
      foreach (var (accountId, email, _) in users)
      {
        var groupNames = await CommonJIRA.GetGroupNamesByAccountIdAsync(httpClient, config, accountId);
        var lastLogin = await CommonAdmin.GetLastActiveDateAsync(httpClient, config, accountId);

        userItems.Add(new UserGroup(email ?? "", groupNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), lastLogin ?? ""));
      }
    }

    var output = new UserGroupList(
      Format: "user-group",
      Version: "1.0",
      CreateDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
      Users: userItems);

    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    return JsonSerializer.Serialize(output, options);
  }
}
