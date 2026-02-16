namespace AtlassianMainteUty;

/// <summary>
/// テナント全ユーザーの所属グループを表示する CLI
/// </summary>
internal static class GetUserGroups
{
  public static async Task<int> Main(string[] args)
  {
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    var users = await CommonAdmin.GetAllOrgUsersAsync(httpClient);
    if (users.Count == 0)
    {
      Console.WriteLine("組織にユーザーが存在しません。");
      return 0;
    }

    Console.WriteLine($"テナント内ユーザー数: {users.Count}");
    Console.WriteLine();

    foreach (var (accountId, email, displayName) in users)
    {
      var groupNames = await CommonJIRA.GetGroupNamesByAccountIdAsync(httpClient, accountId);
      var label = !string.IsNullOrEmpty(email) ? email : (displayName ?? accountId);
      PrintUserGroups(label, accountId, groupNames);
    }

    return 0;
  }

  private static void PrintUserGroups(string userLabel, string accountId, IReadOnlyList<string> groupNames)
  {
    Console.WriteLine($"--- {userLabel} (accountId: {accountId}) ---");
    if (groupNames.Count == 0)
      Console.WriteLine("  所属グループ: （なし）");
    else
    {
      Console.WriteLine($"  所属グループ ({groupNames.Count}):");
      foreach (var name in groupNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"    - {name}");
    }
    Console.WriteLine();
  }
}
