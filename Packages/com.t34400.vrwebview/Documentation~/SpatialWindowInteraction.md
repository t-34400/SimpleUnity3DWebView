preview.51の標準は **YawOnly + 固定傾斜**。移動は上下/左右/奥行き、回転の変更は世界の鉛直軸回りのみ。roll=0、初期pitch=5度（上端を少し奥へ倒す）で、サンプルのSettingsから0〜10度へ調整できる。配置後にheadへ追随しない。旧シーンの既定FaceViewerは実行時にYawOnlyへ移行し、古い保存poseのpitch/rollも固定傾斜へ正規化する。新規作成・移動・Recall・Restoreで同じ規則を使う。FaceViewer/FollowHand/TranslateOnlyのenum値は従来のまま保持する。新しい操作感はQuest未確認。[詳細](BrowserChrome.md)。

以下はpreview.44時点の調査・実装記録。

# 空間ウィンドウ操作の公開資料調査 — 2026-11-10 JST

Meta公式[Meta-Spatial-SDK-Samples](https://github.com/meta-quest/Meta-Spatial-SDK-Samples/tree/822f4d1ec967d4975ae8db386502b60cda5218c0)を調査した。Horizon OS本体の非公開実装と同一とは主張しない。一般向けOS更新ページはこの環境で取得できず、OSのgrab/resizeアルゴリズムを直接確認したわけでもない。

公式Focusサンプルの[PanelsAndInteraction](https://github.com/meta-quest/Meta-Spatial-SDK-Samples/blob/822f4d1ec967d4975ae8db386502b60cda5218c0/Showcases/focus/Documentation/PanelsAndInteraction.md#L82)にはGrabbableType.FACEを使うpanel、PIVOT_Yを使うpanelの例がある。[HomePanelEntity](https://github.com/meta-quest/Meta-Spatial-SDK-Samples/blob/822f4d1ec967d4975ae8db386502b60cda5218c0/PremiumMediaSample/app/src/main/java/com/meta/spatial/samples/premiummediasample/entities/HomePanelEntity.kt#L50)もPIVOT_Yを指定している。enumの内部計算を確認できていないため、名前だけから特定の姿勢追随式を断言しない。Meta側でも用途ごとに配置方針を選んでいることはコードから確認できる。

## 今回の独立したUnity実装

preview.43までのheader grabは平行移動だけで、回転は変化しなかった。preview.44ではBrowserDesktopPanelのPlacement Modeに三方式を設けた。

- FaceViewer（標準）: 掴んだ位置をray上の距離に保持し、移動先とviewerの相対位置に応じてyaw/pitchを調整する。掴み開始の傾きとの差分を保つので開始時に跳ねない。離した後は固定し、常時head追随しない。
- FollowHand: 掴み開始からのhand/controller poseの回転差で姿勢を変える。ProbeCanvasInputModuleはworld rotationを渡すのでrollも扱う。ray方向しかない外部入力ではrollなしのfallback。
- TranslateOnly: 従来の平行移動。姿勢を固定したい場合に選択する。

どれもgrab pointを固定し、Cancelでは位置・回転・sizeを復元する。Horizon OSそっくりという仕様ではなく、このサンプルの明示した操作仕様。resizeは対角固定の既存方式を維持する。

角のhit rectは44×44から標準96×96へ拡大する。Corner Hit Sizeで64–160を選べる。内側は16pxだけに留め、残りを外へ広げることでtab/window close等の中心を覆わない。L字は実際のwindow角に残し、hover時に強調する。

Editor用にgrab anchor・姿勢三方式・Cancel・角の重なりのテストを追加した。クラウドはC# syntaxまでで、実UnityテストとQuestの操作感は未実行。
