using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// user-group-list 形式の JSON ファイルを比較して差分を表示する
/// </summary>
internal static class ChkResult
{
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _ = Config.Load();
      _logger = Log.CreateLogger("ChkResult.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      if (args.Length < 2)
      {
        _logger.LogError("使用法: ChkResult <before.json> <after.json>");
        Console.WriteLine("使用法: ChkResult <before.json> <after.json>");
        return 1;
      }

      var beforePath = args[0].Trim();
      var afterPath = args[1].Trim();

      var exitCode = await execChkResult(beforePath, afterPath);
      return exitCode;
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
  /// before と after の user-group-list JSON を比較し差分を表示する
  /// </summary>
  private static async Task<int> execChkResult(string beforePath, string afterPath)
  {
    var logger = _logger!;

    if (!File.Exists(beforePath))
    {
      logger.LogError("ファイルが見つかりません: {Path}", beforePath);
      Console.WriteLine($"エラー: ファイルが見つかりません: {beforePath}");
      return 1;
    }
    if (!File.Exists(afterPath))
    {
      logger.LogError("ファイルが見つかりません: {Path}", afterPath);
      Console.WriteLine($"エラー: ファイルが見つかりません: {afterPath}");
      return 1;
    }

    UserGroupList? before;
    UserGroupList? after;

    try
    {
      var options = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      var beforeJson = await File.ReadAllTextAsync(beforePath);
      before = JsonSerializer.Deserialize<UserGroupList>(beforeJson, options);

      var afterJson = await File.ReadAllTextAsync(afterPath);
      after = JsonSerializer.Deserialize<UserGroupList>(afterJson, options);
    }
    catch (JsonException ex)
    {
      logger.LogError(ex, "JSON のパースに失敗しました");
      Console.WriteLine($"エラー: JSON のパースに失敗しました - {ex.Message}");
      return 1;
    }

    if (before?.Users == null || after?.Users == null)
    {
      logger.LogError("JSON に users が含まれていません");
      Console.WriteLine("エラー: JSON に users が含まれていません");
      return 1;
    }

    var beforeByMail = before.Users.ToDictionary(u => u.Mail, u => u, StringComparer.OrdinalIgnoreCase);
    var afterByMail = after.Users.ToDictionary(u => u.Mail, u => u, StringComparer.OrdinalIgnoreCase);

    var allMails = beforeByMail.Keys.Union(afterByMail.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var hasDiff = false;

    foreach (var mail in allMails.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
    {
      var inBefore = beforeByMail.TryGetValue(mail, out var beforeUser);
      var inAfter = afterByMail.TryGetValue(mail, out var afterUser);

      if (!inBefore)
      {
        hasDiff = true;
        var msg = $"[追加] {mail}";
        logger.LogInformation(msg);
        Console.WriteLine(msg);
        if (afterUser != null)
        {
          var groups = string.Join(", ", afterUser.CurGroup);
          var detail = $"  curGroup: [{groups}]";
          logger.LogInformation(detail);
          Console.WriteLine(detail);
        }
        continue;
      }

      if (!inAfter)
      {
        hasDiff = true;
        var msg = $"[削除] {mail}";
        logger.LogInformation(msg);
        Console.WriteLine(msg);
        if (beforeUser != null)
        {
          var groups = string.Join(", ", beforeUser.CurGroup);
          var detail = $"  curGroup: [{groups}]";
          logger.LogInformation(detail);
          Console.WriteLine(detail);
        }
        continue;
      }

      // 両方に存在 - curGroup を比較
      var beforeGroups = (beforeUser!.CurGroup ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var afterGroups = (afterUser!.CurGroup ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

      var addedGroups = afterGroups.Except(beforeGroups).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
      var removedGroups = beforeGroups.Except(afterGroups).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();

      if (addedGroups.Count > 0 || removedGroups.Count > 0)
      {
        hasDiff = true;
        var msg = $"[変更] {mail}";
        logger.LogInformation(msg);
        Console.WriteLine(msg);

        if (addedGroups.Count > 0)
        {
          var detail = $"  curGroup [+] 追加: {string.Join(", ", addedGroups)}";
          logger.LogInformation(detail);
          Console.WriteLine(detail);
        }
        if (removedGroups.Count > 0)
        {
          var detail = $"  curGroup [-] 削除: {string.Join(", ", removedGroups)}";
          logger.LogInformation(detail);
          Console.WriteLine(detail);
        }
      }
    }

    if (!hasDiff)
    {
      var msg = "差分はありません。";
      logger.LogInformation(msg);
      Console.WriteLine(msg);
    }

    return 0;
  }
}
