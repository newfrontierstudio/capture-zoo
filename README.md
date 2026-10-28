# CaptureZoo 1.0.13

SNS投稿や、LINE・メールでの画像共有に。画面を切り取り、ペンで丸や矢印、メモを書き込めるスクリーンショットアプリです。画像は編集画面を開いた時と、ペンを離した時に自動でクリップボードへ入ります。

## 画像キャプチャとOCR

- **F9：画像キャプチャ**。範囲を選択して画像を編集します。
- **F10：OCR**。画面上の文字を左ドラッグで囲むと、読み取ったテキストを自動でクリップボードへコピーします。Escで選択を中止できます。
- トレイアイコンを右クリック →「キャプチャのショートカット」「OCRのショートカット」で、それぞれF1〜F12に変更できます。設定は保存され、同じキーの重複割り当てを防ぎます。通常の右ドラッグ操作には干渉しません。

OCRは日本語・英語に対応。初回のみ認識モデル約26MBをダウンロードし、その後はPC内で処理します。画像は外部へ送信しません。

ペンのプレビューと拡大縮小はDirect2DでGPU描画し、非対応環境ではCPU方式に切り替えます。曲線補間、複数の編集画面、元に戻す・やり直し、色とブラシサイズの変更に対応。表示言語は日本語／Englishから選べます。

[最新版をダウンロード](https://github.com/newfrontierstudio/capture-zoo/releases/tag/v1.0.13)

## English

Capture and annotate screenshots for social posts, LINE and email. Images are copied when the editor opens and after each pen stroke.

- **F9: image capture** — select a region and annotate it.
- **F10: OCR** — select text with a left-drag to recognise it and copy it automatically. Press Esc to cancel selection.
- Right-click the tray icon to change **Capture shortcut** and **OCR shortcut** separately (F1–F12). Settings persist across restarts and duplicate assignments are prevented. Ordinary right-drag gestures remain available.

Japanese and English OCR runs locally. About 26 MB of recognition models is downloaded on first use; images are not uploaded. GPU-accelerated pen previews and scaling use Direct2D with a CPU fallback. Multiple editors, undo/redo and Japanese/English interfaces are supported.

Windows 10/11 x64. The installer downloads the Microsoft .NET 8 Desktop Runtime only if needed. Updating preserves settings.

## Build / License

.NET 8 SDK and Inno Setup 6: `scripts/build.ps1`.
[MIT License](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)
