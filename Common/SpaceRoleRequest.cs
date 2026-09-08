namespace AtlassianMainteUty;

/// <summary>
/// space-role-request 形式の JSON 用データ
/// </summary>
public sealed record SpaceRoleRequest(
  string Format,
  string Version,
  string CreateDate,
  IReadOnlyList<SpaceRoleRequestSpace> Spaces);

/// <summary>
/// スペース 1 件分のロール割当リクエスト
/// spaceId が空の場合は spaceKey からスペースを解決する
/// </summary>
public sealed record SpaceRoleRequestSpace(
  string SpaceId,
  string SpaceKey,
  IReadOnlyList<SpaceRoleAssignmentRequest> Assignments);

/// <summary>
/// ロール割当 1 件
/// principalId が空の場合は mail（USER）または groupName（GROUP）から解決する
/// roleId が空の場合は roleName から解決する
/// </summary>
public sealed record SpaceRoleAssignmentRequest(
  string PrincipalType,
  string PrincipalId,
  string Mail,
  string GroupName,
  string RoleId,
  string RoleName);
