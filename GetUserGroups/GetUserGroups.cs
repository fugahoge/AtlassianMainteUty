using System.Reflection;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// テナント全ユーザーの所属グループを表示する
/// </summary>
internal static class GetUserGroups
{
  public static async Task<int> Main(string[] args)
  {
    Config? config = null;
    ILogger? logger = null;

    try
    {
      config = Config.Load();
      logger = LogHelper.CreateLogger("GetUserGroups.log");

      var buildDate = LogHelper.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
      {
        logger.LogInformation("ビルド日時: {BuildDate}", buildDate);
      }

      return await execGetUserGroup(config.Atlassian, logger);
    }
    catch (Exception ex)
    {
      logger?.LogError(ex, "エラーが発生しました");
      return 1;
    }
    finally
    {
      Serilog.Log.CloseAndFlush();
    }
  }

  /// <summary>
  /// テナント全ユーザーの所属グループを表示する。戻り値は 0（成功）または 1（失敗）。Exception がスローされる場合あり。
  /// </summary>
  private static async Task<int> execGetUserGroup(AtlassianConfig config, ILogger logger)
  {
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds) };

    var users = await CommonAdmin.GetAllOrgUsersAsync(httpClient, config);
    if (users.Count == 0)
    {
      logger.LogInformation("組織にユーザーが存在しません。");
      return 0;
    }

    logger.LogInformation("テナント内ユーザー数: {Count}", users.Count);

    foreach (var (accountId, email, displayName) in users)
    {
      var groupNames = await CommonJIRA.GetGroupNamesByAccountIdAsync(httpClient, config, accountId);
      var label = !string.IsNullOrEmpty(email) ? email : (displayName ?? accountId);

      logger.LogInformation("--- {UserLabel} (accountId: {AccountId}) ---", label, accountId);
      if (groupNames.Count == 0)
        logger.LogInformation("  所属グループ: （なし）");
      else
      {
        logger.LogInformation("  所属グループ ({Count}):", groupNames.Count);
        foreach (var name in groupNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
          logger.LogInformation("    - {Name}", name);
      }
    }

    return 0;
  }
}
