# 連携の要求の状態遷移

Editor 連携（開発用のローカルの追加機能）が要求 ID ごとに記録する状態。記録は受け付ける前に書き、Reload と Editor の再起動をまたいで照会できる。

```mermaid
stateDiagram-v2
  [*] --> accepted: 記録してから受け付ける
  accepted --> refused: 実行中の要求がある・手動の Play 中・未保存がある・条件を満たさない
  accepted --> running: 開始
  running --> running: Play に入るときの Reload（記録から引き継ぐ）
  running --> succeeded: 完了して合格
  running --> failed: コード・テスト・環境の失敗
  running --> unknown: 期限切れ・結果が返らない
  running --> interrupted: Editor の再起動
  refused --> [*]
  succeeded --> [*]
  failed --> [*]
  unknown --> [*]
  interrupted --> [*]
```

- 同じ要求 ID の再送は実行し直さず、記録を返す。同じ ID で中身が違えば断る。
- `unknown` と `interrupted` の後は、開始要求を送り直さない。記録と入力のハッシュを確かめてから、新しい要求 ID で始める。
- 成功に数えるのは `succeeded` だけ。テスト 0 件は `failed` にする。
