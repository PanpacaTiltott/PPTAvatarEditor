# PPTAvatarEditor

VRChatアバターの表情編集を中心に、[VRCAvatarEditor 0.8.0](https://github.com/gatosyocora/VRCAvatarEditor) を改変したUnityエディタ拡張です。

## インストール

**[VCCに追加](https://panpacatiltott.github.io/PPTAvatarEditor/)**

登録後、VCCのプロジェクト管理画面で **PPTAvatarEditor** を追加します。Unityの「PPAE」メニューから「Editor」を開きます。

手動で登録する場合は、VCCの「Settings」「Packages」「Add Repository」で次のURLを入力してください。

```text
https://panpacatiltott.github.io/PPTAvatarEditor/index.json
```

## 本家から変更したところ

- **表情編集から開始** — アバターを選ぶとFaceEmotionと顔のプレビューを表示します。
- **編集画面を再配置** — 左にプレビューとFX一覧、右にシェイプキーを配置。境界のドラッグで幅と高さを調整できます。
- **補助機能を集約** — アバター情報・光の当たり方・描画範囲・シェーダーを「アバター設定」にまとめました。
- **保存設定を整理** — 初期保存先は `Assets/PPAE/アバター名/`。保存設定は開いた状態で表示します。
- **日本語を初期設定に** — 追加した選択・保存などのボタンも言語切り替えに対応します。
- **本家と共存** — メニュー名は「PPAE」。パッケージ・名前空間・アセットIDを分離しています。

## 追加機能

- **シェイプキーのUndo / Redo** — 値の調整、保存チェック、クリップの読み込み、リセットを戻したりやり直したりできます。
- **Bodyの見出しからカテゴリを生成** — `---EYE---` など、アバターに付いた区切り名で分類。「未分類」は末尾に表示します。
- **検索と絞り込み** — 名前で検索し、「0 以外」で値の入ったシェイプキーを絞り込めます。

クリップの読み込み・保存・FXステートへの割り当ては、本家の機能を引き継いでいます。保存対象も本家と同じく、展開中のメッシュでチェックした、除外対象外のシェイプキーです。

## 動作環境

Unity 2022.3.22f1、VRChat SDK3 Avatarsで動作確認しています。

## クレジット・ライセンス

**PPTAvatarEditorは、gatosyocora氏のVRCAvatarEditor 0.8.0をPanpacaTiltottが改変したフォークです。**

Copyright (C) 2019 gatosyocora

Zlib Licenseで配布します。元の著作権表示とライセンス全文は [LICENSE.txt](LICENSE.txt) に変更せず保持しています。改変元と改変者を上記に明記し、同梱ソフトウェアのライセンスも保持しています。

ツール内の「ツール情報」でも、改変元・改変者・著作権表示とライセンス原文を確認できます。

開発・連絡先 [@PanpacaTiltott](https://x.com/PanpacaTiltott)
