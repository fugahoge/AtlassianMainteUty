# AtlassianMainteUty

Atlassian Cloud にアクセスしてユーザーをグループに追加する、またはユーザーの所属グループを表示する CLI です。

## 前提条件

- .NET 10.0 SDK
- Atlassian 組織で「新しいユーザー管理」が有効であること
- 追加するユーザーはすでにサイトに招待済みであること

## 設定

| 変数 | 説明 |
|------|------|
| `JiraBaseUrl` | Jira サイトの URL（例: `https://your-domain.atlassian.net`） |
| `AdminApiBaseUrl` | Admin API のベース URL（通常は `https://api.atlassian.com` のまま） |
| `JiraAuthEmail` | Jira API トークンを発行したアカウントのメールアドレス |
| `ApiToken` | [Atlassian の API トークン](https://id.atlassian.com/manage-profile/security/api-tokens)（Jira 用） |
| `ApiKey` | [Atlassian Administration の API キー](https://admin.atlassian.com)（Settings → API keys） |
| `OrgId` | 組織 ID（Atlassian Administration の API キー画面に表示） |

## ビルド

publishディレクトリ内に実行ファイルが生成されます：

```bash
dotnet publish AtlassianMainteUty.sln
```

## 実行

### SetUserToGroup.exe - ユーザーのグループを設定

```bash
.\publish\SetUserToGroup.exe <JSONファイル>
```

- 第一引数で user-group-request 形式の JSON ファイルを指定

### SetUserToGroupBat.exe - ユーザーのグループを設定(バッチファイル版)

SetUserToGroup と同じ仕様（user-group-request 形式の JSON）だが、WebAPI を直接呼び出さず、代わりに curl を実行するバッチファイル（.bat）を出力する。

```bash
.\publish\SetUserToGroupBat.exe <JSONファイル> [出力パス]
```

- 第一引数で JSON ファイルを指定、第二引数で出力パスを指定（省略時は `SetUserToGroup.bat`）
- 生成されたバッチファイルを実行すると、curl で WebAPI と等価な処理を行う
- 前提: curl がインストールされていること、Config.json の設定が正しいこと

### SetUserToGroupPS.exe - ユーザーのグループを設定(PowerShell版)

SetUserToGroup と同じ仕様（user-group-request 形式の JSON）だが、WebAPI を直接呼び出さず、代わりに PowerShell スクリプト（.ps1）を出力する。

```bash
.\publish\SetUserToGroupPS.exe <JSONファイル> [出力パス]
```

- 第一引数で JSON ファイルを指定、第二引数で出力パスを指定（省略時は `SetUserToGroup.ps1`）
- 生成された PowerShell スクリプトを実行すると、Invoke-RestMethod で WebAPI と等価な処理を行う
- accountId/groupId をキャッシュして API 呼び出し回数を削減
- 前提: PowerShell 5.1 以上、Config.json の設定が正しいこと

### GetUserGroups.exe - テナント全ユーザーの所属グループを表示


```bash
.\publish\GetUserGroups.exe
```

## 動作

## リトライ条件

- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)
- HTTP 5xx (サーバーエラー)

上記のいずれかの場合に、３回までリトライします。
