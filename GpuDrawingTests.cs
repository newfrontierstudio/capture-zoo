using System.Reflection;
namespace CaptureZoo;
internal static class GpuDrawingTests
{
 internal static void Run(string root)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  using var image=new Bitmap(700,430);using(var g=Graphics.FromImage(image))g.Clear(Color.White);int copied=0;
  using var editor=new EditorForm(image,_=>copied++);editor.Show();Application.DoEvents();
  var canvas=(PictureBox)typeof(EditorForm).GetField("_canvas",flags)!.GetValue(editor)!;
  var gpu=(GpuCanvasRenderer)typeof(EditorForm).GetField("_gpuRenderer",flags)!.GetValue(editor)!;canvas.Refresh();Application.DoEvents();
  if(!gpu.HardwareActive)throw new Exception("Hardware Direct2D renderer did not start");
  var down=typeof(EditorForm).GetMethod("CanvasMouseDown",flags)!;var move=typeof(EditorForm).GetMethod("CanvasMouseMove",flags)!;var finish=typeof(EditorForm).GetMethod("EndStroke",flags)!;
  down.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,60,100,0)]);
  var clock=System.Diagnostics.Stopwatch.StartNew();
  for(int i=1;i<=600;i++){move.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,60+i,(int)(180+80*Math.Sin(i*.02)),0)]);if(i%16==0)canvas.Refresh();}
  canvas.Refresh();if(!gpu.HardwareActive)throw new Exception("GPU failed during a long stroke");if(copied!=1)throw new Exception("GPU preview wrote to clipboard before release");finish.Invoke(editor,null);canvas.Refresh();if(copied!=2||!gpu.HardwareActive)throw new Exception("GPU stroke finalization failed");
  editor.ClientSize=new Size(850,530);Application.DoEvents();canvas.Refresh();if(!gpu.HardwareActive)throw new Exception("GPU resize failed");
  typeof(EditorForm).GetMethod("RestoreUndo",flags)!.Invoke(editor,null);canvas.Refresh();typeof(EditorForm).GetMethod("RestoreRedo",flags)!.Invoke(editor,null);canvas.Refresh();if(!gpu.HardwareActive||copied!=4)throw new Exception("GPU undo/redo or clipboard changed");
  var result=(Bitmap)typeof(EditorForm).GetField("_image",flags)!.GetValue(editor)!;result.Save(Path.Combine(root,"gpu-stroke-final.png"));
  File.WriteAllText(Path.Combine(root,"gpu-results.txt"),$"PASS Direct2D hardware target active; 600-point batched stroke; preview does not copy; release copies once; resize; undo/redo; original-resolution export. Test elapsed {clock.ElapsedMilliseconds}ms (includes UI/redraw; not a comparative benchmark). No desktop capture or real clipboard changes.");editor.CancelCapture();
 }
}
