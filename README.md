# PPTAvatarEditor

VRChatアバターの表情をつくるUnityエディタ拡張です。顔を見ながらシェイプキーを調整し、アニメーションクリップに保存できます。

初回リリース **0.0.0** に向けて開発中です。

## 表情を編集する

- アバターのシェイプキー名からカテゴリを生成。検索や「0 以外」で絞り込めます。
- FXレイヤーの既存クリップを読み込み、Undo / Redoで表情を調整できます。
- 編集した表情を `.anim` に保存。必要に応じてFXステートにも割り当てられます。

**保存対象は、展開中のメッシュでチェックした項目です。** 閉じたメッシュと除外設定に該当する項目は保存されません。

## 動作環境

Unity 2022.3.22f1、VRChat SDK3 Avatarsで動作確認しています。Unityメニューの「PPAE」から「Editor」を開きます。

VRCAvatarEditorとは別パッケージで共存します。

## クレジット

[VRCAvatarEditor 0.8.0](https://github.com/gatosyocora/VRCAvatarEditor) をベースに開発しています。

Copyright (C) 2019 gatosyocora

Zlib License。元の著作権表示・ライセンス全文・同梱ソフトウェアのライセンスを保持しています。

開発・連絡先 [@PanpacaTiltott](https://x.com/PanpacaTiltott)
