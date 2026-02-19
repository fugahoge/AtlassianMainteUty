# AtlassianMainteUty

Atlassian Cloud にアクセスしてユーザーをグループに追加する、またはユーザーの所属グループを表示する CLI です。

## 前提条件

- .NET 10.0 SDK
- Atlassian 組織で「新しいユーザー管理」が有効であること
- 追加するユーザーはすでにサイトに招待済みであること

## 設定

`Common.cs` で以下を実際の値に書き換えてください。

| 定数 | 説明 |
|------|------|
| `JiraBaseUrl` | Jira サイトの URL（例: `https://your-domain.atlassian.net`） |
| `AdminApiBaseUrl` | Admin API のベース URL（通常は `https://api.atlassian.com` のまま） |
| `JiraAuthEmail` | Jira API トークンを発行したアカウントのメールアドレス |
| `ApiToken` | [Atlassian の API トークン](https://id.atlassian.com/manage-profile/security/api-tokens)（Jira 用） |
| `ApiKey` | [Atlassian Administration の API キー](https://admin.atlassian.com)（Settings → API keys） |
| `OrgId` | 組織 ID（Atlassian Administration の API キー画面に表示） |

## ビルド

publishディレクトリ内に2つの実行ファイルが生成されます：

```bash
dotnet publish AtlassianMainteUty.sln
```

## 実行

### SetUserToGroup.exe - ユーザーのグループを設定

```bash
.\publish\SetUserToGroup.exe
```

### SetUserToGroupBat.exe - curl を実行するバッチファイルを出力

SetUserToGroup と同じ仕様（input.json 形式）だが、WebAPI を直接呼び出さず、代わりに curl を実行するバッチファイル（.bat）を出力する。

```bash
.\publish\SetUserToGroupBat.exe [出力パス]
```

- 出力パスを省略した場合は `SetUserToGroup.bat` に出力
- 生成されたバッチファイルを実行すると、curl で WebAPI と等価な処理を行う
- 前提: curl がインストールされていること、Config.json の設定が正しいこと

### GetUserGroups.exe - テナント全ユーザーの所属グループを表示


```bash
.\publish\GetUserGroups.exe
```

## 動作

### SetUserToGroup.exe

1. input.json からユーザーとグループの追加・削除設定を読み込む。
2. Jira REST API（users/search）でメールから `accountId` を取得。
3. Jira REST API（groupuserpicker）でグループ名から `groupId` を取得。
4. Atlassian Admin API でグループにユーザーを追加。失敗時は最大 3 回までリトライ（HTTP 408/429/5xx の場合、2 秒・4 秒・6 秒の間隔で再試行）。
5. レスポンス JSON を解析し、`accountId`・`groupId`・メッセージ・エラーなどを表示。

### GetUserGroups.exe

- テナント全ユーザーの所属グループを表示
- Jira `groups/picker?accountId=xxx`（所属グループ取得）

## リトライ条件

- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)
- HTTP 5xx (サーバーエラー)

上記のいずれかの場合に、最大 3 回までリトライします。
