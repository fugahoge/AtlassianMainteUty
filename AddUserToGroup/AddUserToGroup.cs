using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// グループにユーザーを追加する
/// </summary>
internal static class AddUserToGroup
{
  public static async Task<int> Main(string[] args)
  {
    Config? config = null;
    ILogger? logger = null;

    try
    {
      config = Config.Load();
      logger = LogHelper.CreateLogger("AddUserToGroup.log");

      var buildDate = LogHelper.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
      {
        logger.LogInformation("ビルド日時: {BuildDate}", buildDate);
      }

      if (args.Length < 2)
      {
        logger.LogError("使用法: AddUserToGroup <メールアドレス> <グループ名>");
        logger.LogInformation("例: AddUserToGroup user@example.com jira-users");
        return 1;
      }

      var email = args[0].Trim();
      var groupName = args[1].Trim();

      if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(groupName))
      {
        logger.LogError("メールアドレスとグループ名は必須です。");
        return 1;
      }

      logger.LogInformation("対象ユーザー: {Email}", email);
      logger.LogInformation("対象グループ: {GroupName}", groupName);

      using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

      var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, config.Atlassian, email);
      if (string.IsNullOrEmpty(accountId))
      {
        logger.LogError("ユーザーが見つかりません (メール: {Email})", email);
        return 1;
      }
      logger.LogInformation("取得した accountId: {AccountId}", accountId);

      var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, config.Atlassian, groupName);
      if (string.IsNullOrEmpty(groupId))
      {
        logger.LogError("グループが見つかりません (名前: {GroupName})", groupName);
        return 1;
      }
      logger.LogInformation("取得した groupId: {GroupId}", groupId);

      logger.LogInformation("グループ追加リクエスト送信（失敗時は最大3回までリトライ）...");
      (var response, var rawResponse) = await CommonAdmin.AddUserToGroupAsync(httpClient, config.Atlassian, accountId, groupId);

      using (response)
      {
        if (!response.IsSuccessStatusCode)
        {
          logger.LogError("グループ追加に失敗しました (HTTP {StatusCode})", (int)response.StatusCode);
          if (!string.IsNullOrEmpty(rawResponse))
            logger.LogError("API レスポンス: {Response}", rawResponse);
          return 1;
        }
      }

      if (!string.IsNullOrEmpty(rawResponse))
      {
        try
        {
          using var doc = JsonDocument.Parse(rawResponse);
          var root = doc.RootElement;

          logger.LogInformation("--- 実行結果 ---");

          if (root.TryGetProperty("account_id", out var accountIdProp))
            logger.LogInformation("  accountId: {AccountId}", accountIdProp.GetString());
          if (root.TryGetProperty("groupId", out var gid))
            logger.LogInformation("  groupId: {GroupId}", gid.GetString());
          if (root.TryGetProperty("id", out var id))
            logger.LogInformation("  membership id: {Id}", id.GetString());

          if (root.TryGetProperty("message", out var msg))
            logger.LogInformation("  メッセージ: {Message}", msg.GetString());
          if (root.TryGetProperty("error", out var err))
            logger.LogInformation("  エラー: {Error}", err.GetString());
          if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
          {
            foreach (var e in errs.EnumerateArray())
            {
              if (e.TryGetProperty("message", out var m))
                logger.LogInformation("  エラー詳細: {Detail}", m.GetString());
            }
          }

          logger.LogInformation("  ユーザー: {Email}", email);
          logger.LogInformation("  グループ: {GroupName}", groupName);
          logger.LogInformation("----------------");
        }
        catch (JsonException)
        {
          logger.LogInformation("レスポンス (生): {Response}", rawResponse);
        }
      }
      else
        logger.LogInformation("処理が正常に完了しました。");

      return 0;
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
}
