namespace AtlassianMainteUty;

/// <summary>
/// user-group 形式の JSON 出力用データ
/// </summary>
public sealed record UserGroupList(
  string Format,
  string Version,
  string CreateDate,
  IReadOnlyList<UserGroup> Users);

/// <summary>
/// ユーザーと所属グループの情報
/// </summary>
public sealed record UserGroup(
  string Mail,
  IReadOnlyList<string> CurGroup,
  string LastLogin);
