# PPTAvatarEditor 開発用メモ

製品概要は [README.md](README.md) を参照してください。初回リリースのバージョンは `0.0.0` です。

## Unityプロジェクトへの配置

VRChat SDK3 Avatarsを導入したUnityプロジェクトを指定します。

```powershell
./tools/Install-UnityPackage.ps1 -ProjectPath 'D:\VCC\YourAvatarProject'
```

専用パッケージ `net.panpacatiltott.pptavatareditor` にコピーします。再配置時は既存のPPAEをプロジェクト外の `PPAE-backups` にバックアップします。Unityで再読み込み後、「PPAE」メニューから「Editor」を開きます。

## 編集と保存

- 上部カテゴリはBodyの区切り名から生成します。「未分類」は末尾です。
- FX一覧は従来の高さで開きます。プレビューとの境界を上下にドラッグして調整できます。
- アバター情報・光の当たり方・描画範囲・シェーダーは、上部の「アバター設定」にまとめています。
- 全メッシュの保存チェックは初期状態でON、Body以外は閉じています。本家と同じく、展開中・チェックON・除外対象外の項目だけを保存します。検索やカテゴリの絞り込みは保存対象を変更しません。
- 保存先の初期値は `Assets/PPAE/<アバター名>/`。同名のクリップは連番で保存します。
- Undo / Redoは値・保存チェック・読み込み・リセットを対象に、直近128操作を保持します。Ctrl+Z、Ctrl+Y / Ctrl+Shift+Z。MacではCommandキーを使います。
- 初期言語はJA。「設定」でJA / EN / KOを切り替え、「設定を保存」で保持します。

## 互換性とライセンス

VRCAvatarEditor 0.8.0とは、名前空間・アセンブリ・GUID・Resources・パッケージIDを分離しています。元のパッケージは変更しません。

同じ選択状態・メッシュの開閉・除外・値からの出力は、本家と14ケースでバイト単位の一致を確認しています。名前順でクリップを読み込むと本家でチェック対象がずれる不具合は、PPAE側では修正済みです。

FaceEmoやMewへのコード依存はありません。Mewは区切り名による分類の仕様のみ参考にしています。

元のZlib Licenseと著作権表示はパッケージ内の `LICENSE.txt` に、同梱ソフトウェアのライセンスは `USING_SOFTWARE_LICENSES.txt` に保持しています。

## VCC向けの公開

パッケージの `package.json` の `version` と配布先 `url`、`RELEASE_NOTES.md` を更新してmasterに反映し、GitHub Actionsの「Publish VPM package」を実行します。ZIP・リリース・VPM一覧・追加ページをまとめて公開します。

GitHub PagesのSourceは「GitHub Actions」を使用します。公開済みのバージョンとZIPは保持し、同じバージョンの内容変更はビルドで拒否します。

ローカルで配布形式を確認する場合は `python tools/build_vpm.py` を実行します。`dist/vpm/` にZIPと一覧を生成します。テキストの改行はLFにそろえ、WindowsとLinuxで同じZIPを生成します。
