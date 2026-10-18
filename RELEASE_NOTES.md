# PPTAvatarEditor 0.3.1

[VCCに追加](https://panpacatiltott.github.io/PPTAvatarEditor/)

撮影する位置を、秒数の入力から「始点」〜「終端」のスライダーで選ぶ操作へ変更しました。

- **クリップ撮影**：「撮影位置」をドラッグすると、選択中のクリップの始点から終端までプレビューを確認できます。
- **表情呼び出し**：選択した表情の「固定位置」も同じ操作に統一しました。秒数の入力欄は表示しません。
- クリップの長さに合わせて選択範囲を制限し、長さ0のクリップでは始点に固定します。

Unity 2022.3.22f1とVRChat SDK3 Avatarsで確認しています。表情呼び出しPrefabの生成にはModular Avatar 1.12以上・2.0未満が必要です。表情エディタ・FX Viewer・クリップ撮影にはModular Avatarは不要です。

保存形式とメニュー生成の仕組みは0.3.0から変更していません。一括撮影では、選択中のクリップで決めた時刻を各クリップに適用し、その時刻より短いクリップは末尾を撮影します。

両版合計216項目のUnity自動検証を通過しました。始点・中間・終端へのドラッグとプレビューのシェイプキー値、長さ0・1秒・4秒のクリップを確認しています。

[撮影と表情メニューの使い方](https://github.com/PanpacaTiltott/PPTAvatarEditor/blob/master/CAPTURE.md)・[検証内容と確認範囲](https://github.com/PanpacaTiltott/PPTAvatarEditor/blob/master/MODULE_VERIFICATION.md)

PPTAvatarEditorはgatosyocora氏のVRCAvatarEditor 0.8.0をPanpacaTiltottが改変したものです。
Copyright (C) 2019 gatosyocora。
変更・追加部分 Copyright (C) 2026 PanpacaTiltott。
原作・変更部分ともにZlib License。同梱の第三者コードにはそれぞれのライセンスが適用されます。
