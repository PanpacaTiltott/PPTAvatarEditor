# PPTAvatarEditor 0.3.0

[VCCに追加](https://panpacatiltott.github.io/PPTAvatarEditor/)

表情を呼び出すメニューと、クリップをPNGに撮影する機能を追加しました。0.0.1からの更新には、これまでの開発版0.1.0・0.2.0の変更も含みます。

- **表情呼び出し**：「PPAE → Expression Menu」から、Modular Avatar対応のExpression.prefabを生成してアバターへ追加。固定中は口パク・まばたき・自動視線を停止し、解除すると通常の制御へ戻します。元のFX・メニュー・Parametersのアセットを置き換えず、元クリップを参照します。
- **FXから自動登録**：左右のFXレイヤーを分けて読み込み、LipSync・Blink・Visemeなどを除外。メニューは「表情呼び出し → FXフォルダ → 表情アニメ」の構成で、ドラッグによる並べ替え・移動とUndoに対応します。「解除」は各表情メニューの1ページ目だけに表示します。
- **アイコンの自動撮影**：生成時にプレビューの構図・画角で各表情を撮影。親メニューには専用のフォルダアイコンを使用します。同期パラメータの追加は、表情数によらずInt型1個・8bitです。
- **クリップ撮影**：「PPAE → Clip Capture」で透過PNGと一括撮影に対応。画像サイズ・撮影時刻・構図を指定でき、FX Viewerから確認中のクリップを引き継げます。
- **FX Viewer**：クリップの差し替えとUndo / Redoに対応。「確認」でHumanoidアバターの姿勢が崩れる問題を修正しました。
- **保存先の整理**：新しい出力をAssets/PPAE/アバター名/内のAnimation・Pictures・Controller・Menu・Icons・Prefabに分けて保存します。

Unity 2022.3.22f1とVRChat SDK3 Avatarsで確認しています。表情呼び出しPrefabの生成にはModular Avatar 1.12以上・2.0未満が必要です。表情エディタ・FX Viewer・クリップ撮影にはModular Avatarは不要です。

既存のクリップ・生成済みアセットは移動しません。開発版で作成した表情メニューへ新しい構成を反映する場合は、更新後に再生成してください。1アバターにつき1つの表情呼び出しPrefabを使用します。

[撮影と表情メニューの使い方](https://github.com/PanpacaTiltott/PPTAvatarEditor/blob/master/CAPTURE.md)・[検証内容と確認範囲](https://github.com/PanpacaTiltott/PPTAvatarEditor/blob/master/MODULE_VERIFICATION.md)

PPTAvatarEditorはgatosyocora氏のVRCAvatarEditor 0.8.0をPanpacaTiltottが改変したものです。
Copyright (C) 2019 gatosyocora。
変更・追加部分 Copyright (C) 2026 PanpacaTiltott。
原作・変更部分ともにZlib License。同梱の第三者コードにはそれぞれのライセンスが適用されます。
