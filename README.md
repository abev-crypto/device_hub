# G600 Studio

Logicool G600（USB ID `046D:C24A`）の本体メモリを編集する Windows x64 アプリです。

1. ZIP を展開し、`G600Studio.exe` を開きます。この PC に入っている .NET 10 Desktop Runtime (x64) を使います。
2. 左側で編集するモードを選びます。G600 の本体メモリには 3 モードあり、それぞれに通常時と G シフト中のボタン割り当てがあります。
3. ボタン、DPI、ライティングを変更したら「本体に保存」を押します。「このモードを使用」でマウスをそのモードに切り替えられます。

G1〜G20 には単一キー、修飾キーとの組み合わせ、マウスボタン、DPI 切り替えなどを割り当てられます。DPI は最大 4 段階で、200〜8200 を 50 刻みで設定できます。LED は通常時と G シフト中の色を指定できます。

Windows 11 22H2 以降では設定画面の余白にアクリルを使用し、背後がぼけて透けます。サイドバー、設定カード、操作ボタンは不透明です。表示は Windows の透明効果設定に従います。

保存前のモードは `%LOCALAPPDATA%\G600Studio\Backups` に自動バックアップされます。元に戻すには対象モードを選び、「バックアップを開く」から以前のファイルを読み込んで「本体に保存」を押します。

## Windows のスタートアップ

左側の「スタートアップに登録」を押すと、次回サインイン時から通知領域に G600 Studio が起動します。アイコンをダブルクリックすると設定画面が開きます。スタートアップ時に使う実行ファイルはこのユーザーのアプリデータにコピーされます。解除は「スタートアップ登録を解除」から行えます。

本体メモリへの保存後はアプリを閉じても割り当てが使えます。自動プロファイル切り替えや複数ステップのマクロはこの版の対象外です。G HUB や Logitech Gaming Software がマウスを操作している場合は、それらの画面を終了して「再読み込み」してください。

## ソースから実行

.NET 10 SDK が入った Windows で `dotnet run --project src/G600Studio.csproj` を実行します。USB 通信には Windows の HID API を使用します。

## 参照

- [Logitech G600 の仕様](https://support.logi.com/hc/en-001/articles/360023465173-Logitech-G600-MMO-Gaming-Mouse-Technical-Specifications)
- [libratbag の G600 レポート構造](https://github.com/libratbag/libratbag/blob/master/src/driver-logitech-g600.c)
- [g600prog の本体メモリ書き込み手順](https://github.com/tulth/g600prog)
