using System.Text.Json;

namespace AtlassianMainteUty;

/// <summary>
/// ユーザーをグループに追加する CLI
/// </summary>
internal static class AddUserToGroup
{
  public static async Task<int> Main(string[] args)
  {
    if (args.Length < 2)
    {
      Console.Error.WriteLine("使用法: AddUserToGroup <メールアドレス> <グループ名>");
      Console.Error.WriteLine("例: AddUserToGroup user@example.com jira-users");
      return 1;
    }

    var email = args[0].Trim();
    var groupName = args[1].Trim();

    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(groupName))
    {
      Console.Error.WriteLine("エラー: メールアドレスとグループ名は必須です。");
      return 1;
    }

    Console.WriteLine($"対象ユーザー: {email}");
    Console.WriteLine($"対象グループ: {groupName}");
    Console.WriteLine();

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    var accountId = await CommonJIRA.GetAccountIdByEmailAsync(httpClient, email);
    if (string.IsNullOrEmpty(accountId))
    {
      Console.Error.WriteLine($"エラー: ユーザーが見つかりません (メール: {email})");
      return 1;
    }
    Console.WriteLine($"取得した accountId: {accountId}");

    var groupId = await CommonJIRA.GetGroupIdByNameAsync(httpClient, groupName);
    if (string.IsNullOrEmpty(groupId))
    {
      Console.Error.WriteLine($"エラー: グループが見つかりません (名前: {groupName})");
      return 1;
    }
    Console.WriteLine($"取得した groupId: {groupId}");
    Console.WriteLine();

    Console.WriteLine("グループ追加リクエスト送信（失敗時は最大3回までリトライ）...");
    (var response, var rawResponse) = await CommonAdmin.AddUserToGroupAsync(httpClient, accountId, groupId);

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        Console.Error.WriteLine($"エラー: グループ追加に失敗しました (HTTP {(int)response.StatusCode})。");
        if (!string.IsNullOrEmpty(rawResponse))
        {
          Console.Error.WriteLine("API レスポンス:");
          Console.Error.WriteLine(rawResponse);
        }
        return 1;
      }
    }

    if (!string.IsNullOrEmpty(rawResponse))
      DisplayResult(rawResponse, email, groupName);
    else
      Console.WriteLine("処理が正常に完了しました。");

    return 0;
  }

  private static void DisplayResult(string rawResponse, string email, string groupName)
  {
    try
    {
      using var doc = JsonDocument.Parse(rawResponse);
      var root = doc.RootElement;

      Console.WriteLine("--- 実行結果 ---");

      if (root.TryGetProperty("account_id", out var accountId))
        Console.WriteLine($"  accountId: {accountId.GetString()}");
      if (root.TryGetProperty("groupId", out var gid))
        Console.WriteLine($"  groupId: {gid.GetString()}");
      if (root.TryGetProperty("id", out var id))
        Console.WriteLine($"  membership id: {id.GetString()}");

      if (root.TryGetProperty("message", out var msg))
        Console.WriteLine($"  メッセージ: {msg.GetString()}");
      if (root.TryGetProperty("error", out var err))
        Console.WriteLine($"  エラー: {err.GetString()}");
      if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
      {
        foreach (var e in errs.EnumerateArray())
        {
          if (e.TryGetProperty("message", out var m))
            Console.WriteLine($"  エラー詳細: {m.GetString()}");
        }
      }

      Console.WriteLine($"  ユーザー: {email}");
      Console.WriteLine($"  グループ: {groupName}");
      Console.WriteLine("----------------");
    }
    catch (JsonException)
    {
      Console.WriteLine("レスポンス (生):");
      Console.WriteLine(rawResponse);
    }
  }
}
