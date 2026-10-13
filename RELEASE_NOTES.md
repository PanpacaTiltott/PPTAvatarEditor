# PPTAvatarEditor 0.0.0

[VCCに追加](https://panpacatiltott.github.io/PPTAvatarEditor/)

VRCAvatarEditor 0.8.0を元にした、PPTAvatarEditorの初回リリースです。

- 表情編集を初期画面にし、プレビュー・FX一覧・シェイプキーの配置を変更。
- プレビューとFX一覧の境界をドラッグしてサイズを調整。
- Bodyの見出しからのカテゴリ生成、名前検索、「0 以外」の絞り込みを追加。
- シェイプキー操作のUndo / Redoを追加。
- 初期保存先を `Assets/PPAE/アバター名/` に変更。
- 補助機能を「アバター設定」に集約し、日本語を初期設定に変更。
- 本家と別パッケージで共存。

Unity 2022.3.22f1 / VRChat SDK3 Avatarsで検証。41項目のUI検証を通過し、同じ出力条件で本家と14ケースのクリップ出力がバイト単位で一致しています。

PPTAvatarEditorはgatosyocora氏のVRCAvatarEditor 0.8.0をPanpacaTiltottが改変したものです。
Copyright (C) 2019 gatosyocora。Zlib License。元のライセンス全文と同梱ソフトウェアのライセンスを保持しています。
