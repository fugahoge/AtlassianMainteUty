namespace AtlassianMainteUty;

/// <summary>
/// user-group-request 形式の JSON 用データ
/// </summary>
public sealed record UserGroupRequest(
  string Format,
  string Version,
  string CreateDate,
  IReadOnlyList<UserGroupRequestItem> Users);

/// <summary>
/// ユーザーのグループ変更リクエスト
/// </summary>
public sealed record UserGroupRequestItem(
  string Mail,
  IReadOnlyList<string> AddGroup,
  IReadOnlyList<string> DelGroup,
  bool DeleteAll);
