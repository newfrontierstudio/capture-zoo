using System.Reflection;
namespace CaptureZoo;
internal static class CaptureImprovementTests
{
 private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 internal static void Run(Bitmap source,string root)
 {
  int copied=0;using(var editor=new EditorForm(source,image=>{copied++;if(image.Size!=source.Size)throw new Exception("Initial copy did not use selected image");})){
   Exception? failure=null;editor.Shown+=(_,_)=>{try{if(copied!=1)throw new Exception("Image was not copied before editor Shown");}catch(Exception error){failure=error;}editor.BeginInvoke((Action)(()=>editor.CancelCapture()));};
   if(editor.ShowDialog()!=DialogResult.Cancel||copied!=1||editor.FinalImage!=null)throw new Exception("Opening copies once; cancellation must not confirm edits");if(failure!=null)throw failure;
  }
  using(var editor=new EditorForm(source,_=>{})){
   var type=typeof(EditorForm);var canvas=(PictureBox)type.GetField("_canvas",Private)!.GetValue(editor)!;canvas.Dock=DockStyle.None;canvas.Size=new Size(320,200);
   type.GetField("_penColor",Private)!.SetValue(editor,Color.Black);type.GetField("_penWidth",Private)!.SetValue(editor,3);
   var mapped=(PointF)type.GetMethod("ImagePoint",Private)!.Invoke(editor,[new Point(31,27)])!;if(mapped.X==Math.Truncate(mapped.X)&&mapped.Y==Math.Truncate(mapped.Y))throw new Exception("Zoom mapping rounded away subpixel coordinates");
   var historyBefore=(List<Bitmap>)type.GetField("_undoHistory",Private)!.GetValue(editor)!;
   type.GetMethod("CanvasMouseDown",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,20,20,0)]);type.GetMethod("EndStroke",Private)!.Invoke(editor,null);if(historyBefore.Count!=0)throw new Exception("Click without a stroke creates a full-image undo copy");
   type.GetMethod("CanvasMouseDown",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,20,20,0)]);
   type.GetMethod("CanvasMouseMove",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,121,91,0)]);var graphics=type.GetField("_strokeGraphics",Private)!.GetValue(editor);
   type.GetMethod("CanvasMouseMove",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,251,149,0)]);
   if(graphics==null||!ReferenceEquals(graphics,type.GetField("_strokeGraphics",Private)!.GetValue(editor)))throw new Exception("Stroke reallocates drawing context per mouse move");type.GetMethod("EndStroke",Private)!.Invoke(editor,null);
   var image=(Bitmap)type.GetField("_image",Private)!.GetValue(editor)!;int blended=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var pixel=image.GetPixel(x,y);if(pixel.R>0&&pixel.R<255&&pixel.R==pixel.G&&pixel.G==pixel.B)blended++;}if(blended<30)throw new Exception("Pen stroke lacks antialiased edge pixels");image.Save(Path.Combine(root,"pen-antialias.png"));
   type.GetMethod("RestoreUndo",Private)!.Invoke(editor,null);if(image.GetPixel(60,45).ToArgb()!=Color.White.ToArgb())throw new Exception("Undo did not remove antialiased stroke");type.GetMethod("RestoreRedo",Private)!.Invoke(editor,null);
   var buttons=(List<Button>)type.GetField("_swatchButtons",Private)!.GetValue(editor)!;var initial=buttons[0].BackColor;Exception? failure=null;using var timer=new System.Windows.Forms.Timer{Interval=80};timer.Tick+=(_,_)=>{var dialog=Application.OpenForms.OfType<ColorPickerDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();try{dialog.SetColor(Color.CornflowerBlue);if(buttons[0].BackColor.ToArgb()!=Color.CornflowerBlue.ToArgb())throw new Exception("Palette does not preview color live");}catch(Exception error){failure=error;}dialog.DialogResult=DialogResult.Cancel;dialog.Close();};timer.Start();type.GetMethod("EditColor",Private)!.Invoke(editor,[0]);if(failure!=null)throw failure;if(buttons[0].BackColor.ToArgb()!=initial.ToArgb())throw new Exception("Cancel did not restore palette");
  }
  var history=new List<Bitmap>();try{var push=typeof(EditorForm).GetMethod("PushHistory",BindingFlags.Static|BindingFlags.NonPublic)!;for(int i=0;i<24;i++)push.Invoke(null,[history,new Bitmap(32,32)]);if(history.Count!=20)throw new Exception("Original 20-step history limit changed");}finally{foreach(var bitmap in history)bitmap.Dispose();}
  File.WriteAllText(Path.Combine(root,"improvement-results.txt"),"PASS initial copy before Shown with fake clipboard; cancellation; fractional zoom coordinates; antialiased pen pixels; stroke context reuse; undo/redo; live HSV picker preview and Cancel restoration; original 20-step history retained; no full-image history copy for a click without drawing. No desktop capture or real clipboard changes.");
 }
}
