using System.Drawing.Imaging;
namespace CaptureZoo;
internal sealed class DirectOcrController : IDisposable
{
 internal Action<bool>? BusyChanged;
 readonly Action<string> notify; CancellationTokenSource? cancellation; long id; Point start,current; Bitmap? desktop; DirectOcrOverlay? overlay; bool ended;
 internal DirectOcrController(Action<string> notification){notify=notification;}
 internal void Accept(DirectGesture update){
  if(update.Kind==DirectGestureKind.Begin){Cancel();id=update.Id;start=update.Start;current=update.Current;ended=false;cancellation=new();BusyChanged?.Invoke(true);CaptureAsync(cancellation);return;}
  if(update.Id!=id||cancellation==null)return;
  current=update.Current;
  if(update.Kind==DirectGestureKind.Cancel){Cancel();return;}
  if(update.Kind==DirectGestureKind.End){ended=true;CloseOverlay();if(desktop!=null)RecogniseAsync(cancellation);}
  else overlay?.Select(start,current);
 }
 async void CaptureAsync(CancellationTokenSource request){
  try{var image=await Task.Run(DesktopSnapshot.Capture);if(request!=cancellation){image.Dispose();return;}desktop=image;if(ended)RecogniseAsync(request);else{overlay=new DirectOcrOverlay(image);overlay.Select(start,current);overlay.Show();}}
  catch(Exception error){AppLog.Error(error);if(request==cancellation){notify(UiLanguage.T("読み取りできませんでした。接続または画像を確認して、もう一度お試しください。"));Cancel();}}
 }
 async void RecogniseAsync(CancellationTokenSource request){
  var frame=desktop;desktop=null;if(frame==null)return;
  try{using(frame){var screen=SystemInformation.VirtualScreen;var bounds=Crop(start,current,screen);if(bounds.Width<4||bounds.Height<4)return;using var image=frame.Clone(bounds,PixelFormat.Format32bppArgb);
   notify(UiLanguage.T("文字を読み取っています…"));await OcrModelStore.EnsureAsync(null,request.Token);var result=await OcrService.ReadAsync(image,request.Token);request.Token.ThrowIfCancellationRequested();if(request!=cancellation)return;
   if(string.IsNullOrWhiteSpace(result.Text)){notify(UiLanguage.T("文字が見つかりませんでした。文字の範囲を大きめに選んでお試しください。"));return;}
   for(int attempt=0;;attempt++){try{Clipboard.SetText(result.Text);break;}catch(System.Runtime.InteropServices.ExternalException) when(attempt<3){await Task.Delay(150,request.Token);request.Token.ThrowIfCancellationRequested();}}
   notify(UiLanguage.T("テキストをコピーしました。"));}}
  catch(OperationCanceledException){}
  catch(Exception error){AppLog.Error(error);if(request==cancellation)notify(UiLanguage.T("読み取りできませんでした。接続または画像を確認して、もう一度お試しください。"));}
  finally{if(request==cancellation)Cancel();request.Dispose();}
 }
 internal static Rectangle Crop(Point a,Point b,Rectangle screen){var rect=Rectangle.Intersect(Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)),screen);if(rect.IsEmpty)return Rectangle.Empty;rect.Offset(-screen.X,-screen.Y);return rect;}
 void CloseOverlay(){overlay?.Close();overlay?.Dispose();overlay=null;}
 internal void Cancel(){var old=cancellation;cancellation=null;old?.Cancel();CloseOverlay();desktop?.Dispose();desktop=null;BusyChanged?.Invoke(false);}
 public void Dispose()=>Cancel();
}
internal sealed class DirectOcrOverlay : Form
{
 readonly Bitmap image;Rectangle selection;
 internal DirectOcrOverlay(Bitmap frame){image=frame;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Bounds=SystemInformation.VirtualScreen;DoubleBuffered=true;}
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x20|0x80;return cp;}}
 internal void Select(Point a,Point b){selection=DirectOcrController.Crop(a,b,Bounds);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.DrawImageUnscaled(image,0,0);using var pen=new Pen(Color.Cyan,2);if(selection.Width>0&&selection.Height>0)e.Graphics.DrawRectangle(pen,selection);}
}
