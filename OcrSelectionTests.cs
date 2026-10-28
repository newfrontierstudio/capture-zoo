using System.Reflection;
namespace CaptureZoo;
internal static class OcrSelectionTests
{
 internal static void Run()
 {
  void Check(bool ok,string message){if(!ok)throw new Exception(message);}
  var expected=new Rectangle(100,50,200,100);Check(OcrSelection.Bounds(new(50,25),new(150,75),new(200,100),new(400,200))==expected,"Zoomed OCR bounds mismatch");
  Check(OcrSelection.Bounds(new(150,75),new(50,25),new(200,100),new(400,200))==expected,"Reverse drag bounds mismatch");
  Check(OcrSelection.Bounds(new(20,70),new(180,140),new(200,200),new(400,200))==new Rectangle(40,40,320,140),"Letterboxed OCR bounds mismatch");
  Check(OcrSelection.Bounds(new(20,70),new(300,300),new(200,200),new(400,200))==new Rectangle(40,40,360,160),"OCR selection must clip to image");
  Check(OcrSelection.Bounds(new(20,20),new(180,140),new(200,200),new(400,200))==null,"Letterbox whitespace must not start a selection");
  Check(!OcrSelection.Dragged(new(10,10),new(11,11))&&OcrSelection.Dragged(new(10,10),new(20,30)),"Right-click and drag threshold mismatch");
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;using var source=new Bitmap(400,200);using(var g=Graphics.FromImage(source))g.Clear(Color.White);source.SetPixel(30,40,Color.Blue);int copied=0,requests=0;Size cropSize=Size.Empty;
  using var editor=new EditorForm(source,_=>copied++);editor.OcrRequestOverride=crop=>{requests++;cropSize=crop.Size;Check(crop.GetPixel(10,10).ToArgb()==Color.Blue.ToArgb(),"OCR crop does not match source coordinates");};
  editor.Show();Application.DoEvents();var canvas=(PictureBox)typeof(EditorForm).GetField("_canvas",flags)!.GetValue(editor)!;canvas.Dock=DockStyle.None;canvas.Size=new Size(400,200);int before=copied;
  var down=typeof(EditorForm).GetMethod("CanvasMouseDown",flags)!;var move=typeof(EditorForm).GetMethod("CanvasMouseMove",flags)!;
  down.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Right,1,20,30,0)]);move.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Right,0,120,90,0)]);
  Check((Rectangle?)typeof(EditorForm).GetField("_ocrSelection",flags)!.GetValue(editor)==new Rectangle(20,30,100,60),"Live OCR rectangle does not match drag");
  var draw=(Func<bool>)canvas.GetType().GetField("RenderGpu",flags)!.GetValue(canvas)!;Check(draw(),"GPU range preview failed");var renderer=(GpuCanvasRenderer)typeof(EditorForm).GetField("_gpuRenderer",flags)!.GetValue(editor)!;Check(renderer.HardwareActive,"Selection overlay must render on the GPU without falling back");
  typeof(Control).GetMethod("OnMouseUp",flags)!.Invoke(canvas,[new MouseEventArgs(MouseButtons.Right,1,120,90,0)]);
  Check(requests==1&&cropSize==new Size(100,60),"Right release must request only the selected crop");Check(copied==before,"OCR selection changed the image clipboard");
  var undo=(List<Bitmap>)typeof(EditorForm).GetField("_undoHistory",flags)!.GetValue(editor)!;Check(undo.Count==0,"OCR range created a pen undo entry");
  down.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Right,1,20,30,0)]);move.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Right,0,120,90,0)]);
  object[] args=[new Message(),Keys.Escape];Check((bool)typeof(EditorForm).GetMethod("ProcessCmdKey",flags)!.Invoke(editor,args)!,"Esc did not consume OCR range cancellation");Check(!(bool)typeof(EditorForm).GetField("_ocrSelecting",flags)!.GetValue(editor)!&&editor.Visible&&requests==1,"Esc must cancel selection without closing the editor or running OCR");
  down.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Right,1,20,30,0)]);typeof(Control).GetMethod("OnMouseUp",flags)!.Invoke(canvas,[new MouseEventArgs(MouseButtons.Right,1,20,30,0)]);var menu=(ContextMenuStrip)typeof(EditorForm).GetField("_canvasMenu",flags)!.GetValue(editor)!;Check(menu.Visible&&requests==1,"Right-click must keep the whole-image OCR menu without running a crop");menu.Close();
  editor.OcrRequestOverride=crop=>cropSize=crop.Size;object[] whole=[new Message(),Keys.Control|Keys.Shift|Keys.O];typeof(EditorForm).GetMethod("ProcessCmdKey",flags)!.Invoke(editor,whole);Check(cropSize==source.Size&&copied==before,"Whole-image OCR shortcut must not alter image clipboard");
  var original=(Bitmap)typeof(EditorForm).GetField("_image",flags)!.GetValue(editor)!;Check(original.GetPixel(60,60).ToArgb()==Color.White.ToArgb(),"Selection overlay was burned into the image");editor.CancelCapture();
 }
}
