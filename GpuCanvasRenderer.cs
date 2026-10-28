using System.Drawing.Imaging;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using Color=System.Drawing.Color;
using Size=System.Drawing.Size;
using Rectangle=System.Drawing.Rectangle;
using D2DPixelFormat=Vortice.DCommon.PixelFormat;
using DrawingPixelFormat=System.Drawing.Imaging.PixelFormat;
using D2DAlphaMode=Vortice.DCommon.AlphaMode;
namespace CaptureZoo;
// Hardware-only Direct2D preview. Uploads the screenshot only when its revision
// changes, never reads GPU pixels back while drawing. Export is committed once
// on pen release, at the original image resolution.
internal sealed class GpuCanvasRenderer:IDisposable
{
 const int BatchSize=192;
 ID2D1Factory? factory;ID2D1HwndRenderTarget? target;ID2D1Bitmap? bitmap;
 ID2D1StrokeStyle? style;ID2D1SolidColorBrush? brush;
 readonly List<ID2D1PathGeometry> batches=[];
 IntPtr window;Size size;long revision=-1;int stroke=-1;bool failed;
 internal bool HardwareActive{get;private set;}
 internal bool Draw(IntPtr hwnd,Size client,Bitmap source,long imageRevision,int strokeId,IReadOnlyList<SmoothStroke.Segment> segments,SmoothStroke.Segment? tail,Color color,float width,Color background,Rectangle? selection=null)
 {
  if(failed||client.Width<=0||client.Height<=0)return false;
  try{
   if(target==null||window!=hwnd){Release();factory=D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded);target=factory.CreateHwndRenderTarget(new RenderTargetProperties(RenderTargetType.Hardware,new D2DPixelFormat(Format.B8G8R8A8_UNorm,D2DAlphaMode.Ignore),96,96,RenderTargetUsage.None,Vortice.Direct2D1.FeatureLevel.Default),new HwndRenderTargetProperties{Hwnd=hwnd,PixelSize=new SizeI(client.Width,client.Height),PresentOptions=PresentOptions.None});window=hwnd;size=client;style=factory.CreateStrokeStyle(new StrokeStyleProperties{StartCap=CapStyle.Round,EndCap=CapStyle.Round,DashCap=CapStyle.Round,LineJoin=Vortice.Direct2D1.LineJoin.Round,MiterLimit=1,DashStyle=DashStyle.Solid});brush=target.CreateSolidColorBrush(ToColor(color));AppLog.Info("Pen preview: Direct2D hardware renderer.");}
   if(size!=client){target.Resize(new SizeI(client.Width,client.Height));size=client;}
   if(revision!=imageRevision){bitmap?.Dispose();bitmap=null;using var converted=new Bitmap(source.Width,source.Height,DrawingPixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(converted))g.DrawImageUnscaled(source,0,0);var data=converted.LockBits(new Rectangle(0,0,converted.Width,converted.Height),ImageLockMode.ReadOnly,DrawingPixelFormat.Format32bppPArgb);try{bitmap=target.CreateBitmap(new SizeI(converted.Width,converted.Height),data.Scan0,(uint)data.Stride,new BitmapProperties(new D2DPixelFormat(Format.B8G8R8A8_UNorm,D2DAlphaMode.Premultiplied),96,96));}finally{converted.UnlockBits(data);}revision=imageRevision;}
   if(stroke!=strokeId||segments.Count<batches.Count*BatchSize){ClearBatches();stroke=strokeId;}
   while((batches.Count+1)*BatchSize<=segments.Count)batches.Add(Geometry(segments,batches.Count*BatchSize,BatchSize,null)!);
   using var changing=Geometry(segments,batches.Count*BatchSize,segments.Count-batches.Count*BatchSize,tail);
   target.BeginDraw();target.Transform=Matrix3x2.Identity;target.Clear(ToColor(background));float scale=Math.Min((float)client.Width/source.Width,(float)client.Height/source.Height);target.Transform=Matrix3x2.CreateScale(scale)*Matrix3x2.CreateTranslation((client.Width-source.Width*scale)/2,(client.Height-source.Height*scale)/2);target.AntialiasMode=AntialiasMode.PerPrimitive;
   target.PushAxisAlignedClip(new Vortice.RawRectF(0,0,source.Width,source.Height),AntialiasMode.Aliased);target.DrawBitmap(bitmap!,1,BitmapInterpolationMode.Linear);brush!.Color=ToColor(color);foreach(var geometry in batches)target.DrawGeometry(geometry,brush,width,style!);if(changing!=null)target.DrawGeometry(changing,brush,width,style!);if(selection is {} rect){var bounds=new Vortice.RawRectF(rect.Left,rect.Top,rect.Right,rect.Bottom);brush.Color=ToColor(Color.FromArgb(40,0,160,220));target.FillRectangle(bounds,brush);brush.Color=ToColor(Color.FromArgb(0,140,210));target.DrawRectangle(bounds,brush,1/Math.Max(.0001f,scale));}
   target.PopAxisAlignedClip();target.EndDraw().CheckError();HardwareActive=true;return true;
  }catch(Exception error){AppLog.Error(error);Release();failed=true;HardwareActive=false;AppLog.Info("Pen preview: CPU fallback (hardware renderer unavailable).");return false;}
 }
 ID2D1PathGeometry? Geometry(IReadOnlyList<SmoothStroke.Segment> segments,int offset,int count,SmoothStroke.Segment? tail)
 {
  if(count==0&&!tail.HasValue)return null;var geometry=factory!.CreatePathGeometry();try{using var sink=geometry.Open();var start=count>0?segments[offset].Start:tail!.Value.Start;sink.BeginFigure(new Vector2(start.X,start.Y),FigureBegin.Hollow);
   void Add(SmoothStroke.Segment s)=>sink.AddBezier(new BezierSegment{Point1=new Vector2(s.C1.X,s.C1.Y),Point2=new Vector2(s.C2.X,s.C2.Y),Point3=new Vector2(s.End.X,s.End.Y)});
   for(int i=0;i<count;i++)Add(segments[offset+i]);if(tail.HasValue)Add(tail.Value);sink.EndFigure(FigureEnd.Open);sink.Close();return geometry;
  }catch{geometry.Dispose();throw;}
 }
 static Color4 ToColor(Color color)=>new(color.R/255f,color.G/255f,color.B/255f,color.A/255f);
 void ClearBatches(){foreach(var geometry in batches)geometry.Dispose();batches.Clear();}
 void Release(){ClearBatches();brush?.Dispose();brush=null;style?.Dispose();style=null;bitmap?.Dispose();bitmap=null;target?.Dispose();target=null;factory?.Dispose();factory=null;revision=-1;stroke=-1;window=IntPtr.Zero;HardwareActive=false;}
 public void Dispose()=>Release();
}
