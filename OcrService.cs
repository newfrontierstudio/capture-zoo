using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.Loader;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.ModelProvider;
namespace CaptureZoo;
internal sealed record OcrTextResult(string Text,int Lines,double Seconds);
internal static class OcrService
{
 static readonly SemaphoreSlim Gate=new(1);static PaddleOcrAll? engine;
 static readonly System.Threading.Timer Idle=new(_=>{if(!Gate.Wait(0))return;try{engine?.Dispose();engine=null;}finally{Gate.Release();}},null,Timeout.Infinite,Timeout.Infinite);
 internal static async Task<OcrTextResult> ReadAsync(Bitmap image,CancellationToken token)
 {
  using var snapshot=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(snapshot)){g.Clear(Color.White);g.DrawImageUnscaled(image,0,0);}
  if((long)snapshot.Width*snapshot.Height>40_000_000)throw new InvalidOperationException(UiLanguage.T("画像が大きすぎます。読み取りたい範囲を小さくしてください。"));
  await Gate.WaitAsync(token).ConfigureAwait(false);
  try{Idle.Change(Timeout.Infinite,Timeout.Infinite);return await Task.Run(async()=>{
   token.ThrowIfCancellationRequested();if(!OcrModelStore.Ready())throw new InvalidOperationException(UiLanguage.T("OCRモデルの準備が必要です。もう一度実行してください。"));
   if(engine==null){foreach(var a in OcrModelStore.Assets)AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(OcrModelStore.Root,a.File)));
    var type=Type.GetType("Sdcb.SimdPaddleOCR.Models.ChineseV6Small.ChineseV6SmallModels, Sdcb.SimdPaddleOCR.Models.ChineseV6Small",true)!;
    var bundle=(PaddleOcrModelBundle)type.GetProperty("Default",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
    engine=await PaddleOcrAll.LoadAsync(bundle,new PaddleOcrOptions{LineWorkerCount=2,DetIntraOpThreads=Math.Min(4,Environment.ProcessorCount),Detector=new(){LimitSideLength=1920,MaxPooledSessions=1},Recognizer=new(){MaxPooledSessions=2}},token).ConfigureAwait(false);}
   token.ThrowIfCancellationRequested();var clock=System.Diagnostics.Stopwatch.StartNew();var data=snapshot.LockBits(new Rectangle(0,0,snapshot.Width,snapshot.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   try{var result=Run(engine,data,snapshot.Width,snapshot.Height);token.ThrowIfCancellationRequested();return new OcrTextResult(result.Text,result.Lines.Length,clock.Elapsed.TotalSeconds);}finally{snapshot.UnlockBits(data);}
  },token).ConfigureAwait(false);}finally{Idle.Change(TimeSpan.FromMinutes(2),Timeout.InfiniteTimeSpan);Gate.Release();}
 }
 static unsafe PaddleOcrResult Run(PaddleOcrAll ocr,BitmapData data,int width,int height)=>ocr.Run(new ReadOnlySpan<byte>((void*)data.Scan0,checked(data.Stride*height)),width,height,data.Stride,ImagePixelFormat.Bgra32);
}
