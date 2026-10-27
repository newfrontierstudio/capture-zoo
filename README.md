# CaptureZoo 1.0.8

SNS投稿や、LINE・メールでの画像共有に。F10で画面の範囲を選び、ペンで丸や矢印、メモを書き込めるスクリーンショットアプリです。

画像は自動でクリップボードに入るので、共有先へ貼り付けて使えます。

- 速く描いた線を曲線で補間し、細かな入力の揺れと角張りを抑えます。細いペンにも曲線近似を適用します。
- ペンのプレビューと画像の拡大縮小はDirect2DでGPU描画します。非対応環境ではCPU方式へ切り替えます。
- 画像を開いた時と、ペンを離した時にクリップボードを更新します。
- 日本語／Englishに対応。Windowsの表示言語に合わせ、トレイのLanguageメニューから切り替えられます。
- 複数の編集画面、元に戻す・やり直し、色とブラシサイズの変更に対応。
- HDR画面の取得にはDirect3D 11を使用し、対応しない環境では通常方式で取得します。

## English

Create annotated screenshots for social posts, LINE, and email. Press F10 to select a screen region, draw circles, arrows, or notes, and paste the image into a supported app. The image is copied when the editor opens and after each pen stroke. Closing the editor keeps the current clipboard contents.

Pen preview and image scaling use hardware-accelerated Direct2D, with a CPU fallback. Fast strokes use smooth curve interpolation and input stabilization. Multiple editors, undo/redo, and live color previews are supported. The interface follows your Windows display language; choose Japanese or English from the tray Language menu.

Windows 10/11 x64. The installer downloads the Microsoft .NET 8 Desktop Runtime only if needed. Settings are kept when updating.

## Build / License

.NET 8 SDK and Inno Setup 6: `scripts/build.ps1`.
[MIT License](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)
