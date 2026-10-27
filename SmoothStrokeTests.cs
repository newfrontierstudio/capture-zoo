using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
namespace CaptureZoo;
internal static class SmoothStrokeTests
{
 internal static void Run(string root)
 {
  void Check(bool ok,string text){if(!ok)throw new Exception(text);}
  var input=Enumerable.Range(0,13).Select(i=>{double angle=i*Math.PI/6;return new PointF(150+100*(float)Math.Cos(angle),150+100*(float)Math.Sin(angle));}).ToArray();
  var smoother=new SmoothStroke();var curves=new List<SmoothStroke.Segment>();smoother.Start(input[0]);Check(smoother.Add(input[1])==null&&smoother.Preview!=null,"First movement must preview immediately");
  for(int i=2;i<input.Length;i++){var curve=smoother.Add(input[i]);if(curve.HasValue)curves.Add(curve.Value);}curves.Add(smoother.Preview!.Value);
  Check(curves.Count==input.Length-1,"Missing curve or endpoint");double curveError=0,lineError=0;int samples=0;
  PointF At(SmoothStroke.Segment s,float t){float u=1-t;return new(u*u*u*s.Start.X+3*u*u*t*s.C1.X+3*u*t*t*s.C2.X+t*t*t*s.End.X,u*u*u*s.Start.Y+3*u*u*t*s.C1.Y+3*u*t*t*s.C2.Y+t*t*t*s.End.Y);}
  double Error(PointF p)=>Math.Abs(Math.Sqrt((p.X-150)*(p.X-150)+(p.Y-150)*(p.Y-150))-100);
  for(int i=1;i<curves.Count-1;i++){for(int n=1;n<10;n++){float t=n/10f;curveError+=Error(At(curves[i],t));lineError+=Error(new PointF(input[i].X*(1-t)+input[i+1].X*t,input[i].Y*(1-t)+input[i+1].Y*t));samples++;}var a=curves[i-1];var b=curves[i];Check(a.End==b.Start,"Disconnected segments");float ax=a.End.X-a.C2.X,ay=a.End.Y-a.C2.Y,bx=b.C1.X-b.Start.X,by=b.C1.Y-b.Start.Y;Check(Math.Abs(ax*by-ay*bx)<.02,"Curve tangent has a sharp join");}
  Check(curveError<lineError*.35,"Sparse curve is not smoother than straight connections");
  using(var before=new Bitmap(300,300))using(var after=new Bitmap(300,300))using(var pen=new Pen(Color.Magenta,8){StartCap=LineCap.Round,EndCap=LineCap.Round}){
   using(var g=Graphics.FromImage(before)){g.Clear(Color.White);g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawLines(pen,input);}using(var g=Graphics.FromImage(after)){g.Clear(Color.White);g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var s in curves)s.Draw(g,pen);}before.Save(Path.Combine(root,"sparse-before.png"));after.Save(Path.Combine(root,"sparse-after.png"));}
  var bounded=new SmoothStroke();bounded.Start(new(0,0));for(int i=1;i<20000;i++){var s=bounded.Add(new(i%500,30*MathF.Sin(i*.1f)));if(s.HasValue)Check(float.IsFinite(s.Value.C1.X)&&float.IsFinite(s.Value.C2.Y),"Non-finite curve control");}Check(((List<PointF>)typeof(SmoothStroke).GetField("points",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(bounded)!).Count<=3,"Stroke history is unbounded");
  var jitter=new SmoothStroke();jitter.Start(new(0,100));double rawNoise=0,filteredNoise=0;
  for(int i=1;i<400;i++){float y=100+(i%2==0?2:-2);jitter.Add(new(i*2,y));if(jitter.Preview is {} preview){rawNoise+=2;filteredNoise+=Math.Abs(preview.End.Y-100);}}
  Check(filteredNoise<rawNoise*.65,"Input jitter is not reduced");var endpoint=new PointF(801,100);jitter.Add(endpoint,true);Check(jitter.Preview!.Value.End==endpoint,"Release must reach the exact pointer endpoint");
  var approximation=new SmoothStroke{ApproximationTolerance=2.25f};approximation.Start(new(0,0));approximation.Add(new(40,0));var softened=approximation.Add(new(40,40))!.Value;
  float displacement=MathF.Sqrt(MathF.Pow(softened.End.X-40,2)+MathF.Pow(softened.End.Y,2));
  Check(displacement>1&&displacement<=2.251f,"Abrupt input corner must be approximated within the configured limit");
  Check(softened.Start==new PointF(0,0)&&softened.End==approximation.Preview!.Value.Start,"Approximation must retain the first point and connected tail");
  approximation.Add(new(40,40),true);Check(approximation.Preview!.Value.End==new PointF(40,40),"Approximation must retain the last pointer endpoint");
  long Tick(int ms)=>(long)(ms/1000d*System.Diagnostics.Stopwatch.Frequency);
  var deliberate=new SmoothStroke();deliberate.Start(new(0,0),Tick(0));deliberate.Add(new(40,0),timestamp:Tick(20));var corner=deliberate.Add(new(40,40),timestamp:Tick(180))!.Value;
  Check(corner.End==new PointF(40,0)&&deliberate.Preview!.Value.Start==corner.End,"Paused intentional corner must remain exact");
  Check(corner.C2.Y==0&&deliberate.Preview!.Value.C1.X==40,"Intentional corner handles must follow each straight side");
  string? prior=UiLanguage.TestLanguage;
  try{
   Check(UiLanguage.EffectiveLanguage("auto",CultureInfo.GetCultureInfo("ja-JP"))=="ja"&&UiLanguage.EffectiveLanguage("auto",CultureInfo.GetCultureInfo("en-US"))=="en"&&UiLanguage.EffectiveLanguage("en",CultureInfo.GetCultureInfo("ja-JP"))=="en","Language selection failed");
   UiLanguage.TestLanguage="en";using var picker=new ColorPickerDialog(Color.Magenta);Check(picker.Text=="Pen color"&&((Button)picker.AcceptButton!).Text=="Apply"&&((Button)picker.CancelButton!).Text=="Cancel","Color picker is not fully translated");picker.Show();Application.DoEvents();using(var shot=new Bitmap(picker.Width,picker.Height)){picker.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(root,"english-picker.png"));}picker.Close();
   using var source=new Bitmap(500,300);int copies=0;using var editor=new EditorForm(source,_=>copies++);var flags=BindingFlags.NonPublic|BindingFlags.Instance;var canvas=(PictureBox)typeof(EditorForm).GetField("_canvas",flags)!.GetValue(editor)!;canvas.Dock=DockStyle.None;canvas.Size=new Size(500,300);
   var swatches=(List<Button>)typeof(EditorForm).GetField("_swatchButtons",flags)!.GetValue(editor)!;Check(swatches[0].AccessibleName=="Color 1","Palette is not translated");UiLanguage.TestLanguage="ja";editor.ApplyLanguage();Check(swatches[0].AccessibleName=="色 1","Live Japanese switch failed");UiLanguage.TestLanguage="en";editor.ApplyLanguage();Check(swatches[0].AccessibleName=="Color 1","Live English switch failed");
   var down=typeof(EditorForm).GetMethod("CanvasMouseDown",flags)!;var move=typeof(EditorForm).GetMethod("CanvasMouseMove",flags)!;var finish=typeof(EditorForm).GetMethod("EndStroke",flags)!;
   down.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,30,30,0)]);move.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,90,100,0)]);Check(copies==0,"Preview copied before release");move.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,180,30,0)]);finish.Invoke(editor,null);finish.Invoke(editor,null);Check(copies==1,"Smoothed stroke must copy exactly once");
  }finally{UiLanguage.TestLanguage=prior;}
  File.WriteAllText(Path.Combine(root,"smooth-language-results.txt"),$"PASS sparse curve error {curveError/samples:0.000}px vs straight {lineError/samples:0.000}px; continuous joins; immediate tail preview; correct endpoint; bounded history after 20000 points; Japanese/English switching; English picker; copy exactly once. No real clipboard changes.");
 }
}
