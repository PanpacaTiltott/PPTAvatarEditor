PPTAvatarEditor 0.1.0

VRChatアバターの表情を編集し、アニメーションクリップに保存。

Unityの「PPAE」メニューから「Editor」を開き、アバターを選びます。
FX一覧の「編集」でクリップを読み込み、シェイプキーを調整して「.anim を保存」。
保存対象は、展開中のメッシュでチェックした項目です。除外設定に該当する項目は保存されません。
初期保存先は Assets/PPAE/アバター名/ です。

FX一覧の高さはプレビューとの境界のドラッグで調整できます。
アバター情報・光の当たり方・描画範囲・シェーダーは「アバター設定」にまとめています。
元に戻す Ctrl+Z　やり直す Ctrl+Y / Ctrl+Shift+Z（MacはCommand）

GitHub https://github.com/PanpacaTiltott/PPTAvatarEditor
連絡先 @PanpacaTiltott

表情呼び出し
Modular Avatar 1.12以上、2.0未満を導入すると「PPAE / Expression Menu」を使用できます。
アバターと表情クリップを選び「Prefabを生成」「アバターに追加」で設定します。
初期保存先は Assets/PPAE/アバター名/ExpressionMenus/ です。
メニューで選んだ表情を固定し、「解除」か選択項目のOFFで戻します。
固定中は口パク・まばたき・自動視線を停止します。
入力はシェイプキーのみのクリップです。対象メッシュの未記録キーは生成時の値で補完します。
既存FX・メニュー・Parametersを保持し、追加Prefabを外すと元の構成になります。
1アバターに1つの表情呼び出しPrefabを使用してください。

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
