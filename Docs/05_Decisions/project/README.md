# ADR の一覧（この案件）

この案件の技術・構成の決定。新しいものを下に足す。共通ルールの決定は [../rules/README.md](../rules/README.md)。

| 番号 | 題名 | 状態 | 要約 |
|---|---|---|---|
| [0001](0001-develop-package-under-assets.md) | パッケージを Assets/Yakimannzyuu/YakimaTank で開発する | 承認 | Assets の下で Git に入れるのはこのフォルダだけ。導入先のパスは変わる |
| [0002](0002-semver-in-package-json.md) | 版は SemVer とし package.json を唯一の版とする | 承認 | rules-adr 0004 を置き換え。0.x で開発し、公開で 1.0.0 |
| [0003](0003-public-from-start-git-url.md) | 開発中から公開し、確認版は git URL で配る | 承認 | 非公開メモは Git に入れない。更新は同じ URL を指定し直す |
| [0004](0004-vpm-at-release.md) | VPM 配布はリリース時に整える | 承認 | 今は vpmDependencies と CHANGELOG の準備だけ |
| [0005](0005-mit-license.md) | ライセンスを MIT とする | 承認 | 戦車モデル本体は含めず、別のライセンス |
| [0006](0006-no-devcontainer-local-skills.md) | devcontainer を使わず、同梱スキルは Git に入れない | 承認 | rules-adr 0009・0018 を置き換え。ponytail はローカルだけ |
| [0007](0007-ai-verification-in-running-editor.md) | 起動済みの Editor で AI が検証する | 承認 | ローカル連携で AI がコンパイル・テスト・生成・ビルド。付録 U の 2 項目と 8 章の役割を置き換え |
| [0008](0008-settings-as-source-shared-generation.md) | 設定アセットを正本にして、生成とワールドの更新を共通にする | 承認 | ウィンドウと AI が同じ処理。非破壊の生成をその場で更新。Play 中は C・A 案＋W2 |
| [0009](0009-driver-owned-custom-vehicle-sync.md) | 運転手が所有する独自同期と安定性優先の車両間衝突を採用する | 承認 | 運転手がオーナーで自車だけを計算し、同期変数で送る。車両同士は各オーナーが自車だけに掛ける簡易衝突 |
