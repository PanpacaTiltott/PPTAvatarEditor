PPTAvatarEditor 0.3.1

VRChatアバターの表情を編集し、アニメーションクリップに保存。

Unityの「PPAE」メニューから「Editor」を開き、アバターを選びます。
FX一覧の「編集」でクリップを読み込み、シェイプキーを調整して「.anim を保存」。
保存対象は、展開中のメッシュでチェックした項目です。除外設定に該当する項目は保存されません。
初期保存先は Assets/PPAE/アバター名/Animation/ です。

FX一覧の高さはプレビューとの境界のドラッグで調整できます。
アバター情報・光の当たり方・描画範囲・シェーダーは「アバター設定」にまとめています。
元に戻す Ctrl+Z　やり直す Ctrl+Y / Ctrl+Shift+Z（MacはCommand）

GitHub https://github.com/PanpacaTiltott/PPTAvatarEditor
連絡先 @PanpacaTiltott

表情呼び出し
Modular Avatar 1.12以上、2.0未満を導入すると「PPAE / Expression Menu」を使用できます。
表情と構図をプレビューで確認し、「生成してアバターに追加」を押します。アイコンも自動撮影します。
保存先 Assets/PPAE/アバター名/ 内のController、Menu、Icons、Prefab。メニューはアバターメニュー → 表情呼び出し → FXフォルダ → 表情アニメ。
メニューで選んだ表情を固定し、「解除」か選択項目のOFFで戻します。
固定中は口パク・まばたき・自動視線を停止します。
元のFXクリップを直接参照し、メニュー用の.animは生成しません。未記録のキーは既存FXの制御を使用します。
既存FX・メニュー・Parametersを保持し、追加Prefabを外すと元の構成になります。
1アバターに1つの表情呼び出しPrefabを使用してください。

クリップ撮影
「PPAE / Clip Capture」でクリップを追加し、構図を決めてPNGを保存します。
初期設定は1024x1024、背景透過、撮影位置は始点。「始点」から「終端」までスライダーで調整できます。
左ドラッグで回転、ホイールでズーム、右ドラッグで位置を調整します。
「FXから追加」でレイヤー内のクリップを読み込めます。
保存先は Assets/PPAE/アバター名/Pictures/。同名PNGは上書きせず連番で保存します。
FX Viewerの「撮影」からは選択クリップと時刻を引き継ぎます。
Expression Menuはアバター選択時に顔用のシェイプキーFXを自動登録します。
生成時に各表情の256x256アイコンを撮影します。単独撮影したPNGと撮影時刻も一覧へ反映します。
クリップ名と画像はアニメーションから自動で反映します。撮影にModular Avatarは不要です。
クリップ単体を撮影します。FXの遷移・レイヤー合成やランタイムスクリプトは再現しません。

設計方針
追加機能は独立したモジュールとして導入できる形を基本とします。
バージョンはSemantic Versioning 2.0.0のMajor.Minor.Patchに従います。
詳細 https://github.com/PanpacaTiltott/PPTAvatarEditor/blob/master/DESIGN.md

PPTAvatarEditor は VRCAvatarEditor 0.8.0 の改変版です。
原作者 gatosyocora ／ 改変 PanpacaTiltott
Copyright (C) 2019 gatosyocora
変更・追加部分 Copyright (C) 2026 PanpacaTiltott
原作・変更部分ともに Zlib License

原作のライセンス全文は LICENSE.txt、変更・追加部分は MODIFICATIONS_LICENSE.txt を参照してください。
変更・追加部分のZlib許諾は0.0.0にも適用します。
第三者コードには個別のライセンスが引き続き適用されます。USING_SOFTWARE_LICENSES.txt を参照してください。

生成メニューは表情呼び出し、FXフォルダ、表情アニメの3階層。カテゴリは編集画面内の整理に使用し、順序を反映。「解除」は各FXフォルダの1ページ目だけに配置。LipSync・Blink・Visemeなどは自動登録から除外します。画角調整に対応し、親メニューは専用のフォルダアイコンを使用します。生成Prefab名はExpressionです。
