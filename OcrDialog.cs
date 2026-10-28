namespace CaptureZoo;
internal sealed class OcrDialog:Form
{
 readonly Bitmap source;readonly CancellationTokenSource cancellation=new();readonly RichTextBox text=new(){Dock=DockStyle.Fill,DetectUrls=false,WordWrap=true,BorderStyle=BorderStyle.FixedSingle};
 readonly Label status=new(){AutoSize=true,MaximumSize=new Size(800,0),Margin=new Padding(0,0,0,10)};
 readonly Button copy=new(){AutoSize=true,Enabled=false},close=new(){AutoSize=true};readonly Action<string> copyText;
 readonly Func<Bitmap,IProgress<(long Done,long Total)>,CancellationToken,Task<OcrTextResult>> read;
 internal OcrDialog(Bitmap image,Action<string>? copyText=null,Func<Bitmap,IProgress<(long Done,long Total)>,CancellationToken,Task<OcrTextResult>>? read=null)
 {
  source=new Bitmap(image);this.copyText=copyText??Clipboard.SetText;this.read=read??Read;
  Text=UiLanguage.T("文字を読み取る（OCR）");Icon=AppIcon.Load();Font=new Font(UiLanguage.English?"Arial":"Meiryo UI",10);ClientSize=new Size(850,560);MinimumSize=new Size(480,300);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
  text.Font=new Font(UiLanguage.English?"Arial":"Meiryo UI",11);text.AccessibleName=UiLanguage.T("読み取り結果（編集できます）");text.ReadOnly=true;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  status.Text=UiLanguage.T("日本語・English ／ PC内で読み取ります。初回のみ約26MBをダウンロードします。");layout.Controls.Add(status,0,0);layout.Controls.Add(text,0,1);
  copy.Text=UiLanguage.T("テキストをコピー");close.Text=UiLanguage.T("閉じる");var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,10,0,0)};buttons.Controls.AddRange([close,copy]);layout.Controls.Add(buttons,0,2);Controls.Add(layout);
  close.Click+=(_,_)=>Close();CancelButton=close;
  copy.Click+=(_,_)=>{try{this.copyText(text.Text);status.Text=UiLanguage.T("テキストをコピーしました。");}catch{status.Text=UiLanguage.T("コピーできませんでした。もう一度お試しください。");}};
  text.TextChanged+=(_,_)=>copy.Enabled=!text.ReadOnly&&!string.IsNullOrWhiteSpace(text.Text);
  FormClosing+=(_,_)=>cancellation.Cancel();Shown+=async(_,_)=>await StartAsync();
 }
 static async Task<OcrTextResult> Read(Bitmap image,IProgress<(long Done,long Total)> progress,CancellationToken token){await Task.Run(()=>OcrModelStore.EnsureAsync(progress,token),token);return await OcrService.ReadAsync(image,token);}
 async Task StartAsync()
 {
  var progress=new Progress<(long Done,long Total)>(p=>{if(IsDisposed||cancellation.IsCancellationRequested)return;status.Text=p.Done<p.Total?UiLanguage.F("OCRの準備中… {0:0.0} / {1:0.0} MB",p.Done/1048576d,p.Total/1048576d):UiLanguage.T("文字を読み取っています…");});
  try{var result=await read(source,progress,cancellation.Token);if(IsDisposed||cancellation.IsCancellationRequested)return;text.ReadOnly=false;text.Text=result.Text;copy.Enabled=!string.IsNullOrWhiteSpace(result.Text);status.Text=copy.Enabled?UiLanguage.F("{0}行を読み取りました。内容を確認・修正してコピーできます。",result.Lines):UiLanguage.T("文字が見つかりませんでした。文字の範囲を大きめに選んでお試しください。");text.Focus();}
  catch(OperationCanceledException){}
  catch(Exception error){if(!IsDisposed&&!cancellation.IsCancellationRequested){status.Text=UiLanguage.T("読み取りできませんでした。接続または画像を確認して、もう一度お試しください。");AppLog.Info("OCR failed: "+error.GetType().Name);}}
 }
 protected override void Dispose(bool disposing){if(disposing){cancellation.Cancel();source.Dispose();}base.Dispose(disposing);}
}
