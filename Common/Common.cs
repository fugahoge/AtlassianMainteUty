namespace AtlassianMainteUty;

/// <summary>
/// Atlassian Cloud / Jira 用の設定（ハードコード）。
/// 実行前に実際の値に置き換えてください。
/// </summary>
public static class Common
{
  public const string JiraBaseUrl = "https://your-domain.atlassian.net";
  public const string AdminApiBaseUrl = "https://api.atlassian.com";
  public const string JiraAuthEmail = "admin@example.com";
  public const string ApiToken = "YOUR_JIRA_API_TOKEN";
  public const string ApiKey = "YOUR_ATLASSIAN_ADMIN_API_KEY";
  public const string OrgId = "YOUR_ORGANIZATION_ID";
}
