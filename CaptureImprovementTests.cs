using System.Reflection;
namespace CaptureZoo;
internal static class CaptureImprovementTests
{
 static IEnumerable<Control> Desc(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Desc(c)));
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
  int updates=0;
  using(var editor=new EditorForm(source,_=>updates++))
  using(var second=new EditorForm(source,_=>updates++)){
   editor.Show();second.Show();Application.DoEvents();
   if(!editor.Visible||!second.Visible||updates!=2)throw new Exception($"Independent editors: visible={editor.Visible}/{second.Visible}, copies={updates}");
   var type=typeof(EditorForm);var canvas=(PictureBox)type.GetField("_canvas",Private)!.GetValue(editor)!;
   type.GetMethod("CanvasMouseDown",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,20,20,0)]);
   type.GetMethod("CanvasMouseMove",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,80,50,0)]);
   if(updates!=2)throw new Exception("Clipboard updated before pen release");
   type.GetMethod("EndStroke",Private)!.Invoke(editor,null);
   if(updates!=3)throw new Exception("Pen release must copy exactly once despite capture loss");
   type.GetMethod("EndStroke",Private)!.Invoke(editor,null);
   if(updates!=3)throw new Exception("Repeated stroke cleanup copied again");
   type.GetMethod("RestoreUndo",Private)!.Invoke(editor,null);
   type.GetMethod("RestoreRedo",Private)!.Invoke(editor,null);
   if(updates!=5)throw new Exception("Undo and redo did not refresh clipboard");
   type.GetMethod("CanvasMouseDown",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,1,20,20,0)]);
   type.GetMethod("CanvasMouseMove",Private)!.Invoke(editor,[canvas,new MouseEventArgs(MouseButtons.Left,0,90,60,0)]);
   second.Close();editor.Close();
   if(updates!=5)throw new Exception("Closing an editor overwrote the shared clipboard");
  }
  var history=new List<Bitmap>();try{var push=typeof(EditorForm).GetMethod("PushHistory",BindingFlags.Static|BindingFlags.NonPublic)!;for(int i=0;i<24;i++)push.Invoke(null,[history,new Bitmap(32,32)]);if(history.Count!=10)throw new Exception("History must be capped at ten steps");}finally{foreach(var bitmap in history)bitmap.Dispose();}
  using(var editor=new EditorForm(source,_=>{})){
   var undo=(List<Bitmap>)typeof(EditorForm).GetField("_undoHistory",Private)!.GetValue(editor)!;var redo=(List<Bitmap>)typeof(EditorForm).GetField("_redoHistory",Private)!.GetValue(editor)!;
   undo.Add(new Bitmap(source));redo.Add(new Bitmap(source));editor.Show();Application.DoEvents();editor.ClientSize=new Size(220,160);Application.DoEvents();
   var button=Desc(editor).OfType<Button>().Single(b=>b.Text=="OCR");
   using(var preview=new Bitmap(editor.Width,editor.Height)){editor.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));preview.Save(Path.Combine(root,"ocr-narrow.png"));}
   if(!button.Visible||button.Parent==null||button.Left<0||button.Right>button.Parent.ClientSize.Width||button.Parent.Right>editor.ClientSize.Width)throw new Exception($"OCR button is clipped in narrow editor: visible={button.Visible}, parent={button.Parent?.Name}/{button.Parent?.Bounds}, button={button.Bounds}, editor={editor.ClientSize}");
   if(TextRenderer.MeasureText(button.Text,button.Font).Width>button.Width-button.Padding.Horizontal-8)throw new Exception("OCR label lacks room");
   editor.Close();if(undo.Count!=0||redo.Count!=0)throw new Exception("Closing editor retained undo/redo images");
  }
  using(var reopened=new EditorForm(source,_=>{})){foreach(string field in new[]{"_undoHistory","_redoHistory"})if(((List<Bitmap>)typeof(EditorForm).GetField(field,Private)!.GetValue(reopened)!).Count!=0)throw new Exception("New editor restored old history");}
  File.WriteAllText(Path.Combine(root,"improvement-results.txt"),"PASS release-to-copy exactly once; no copy on close, even unfinished stroke; simultaneous independent editors; undo/redo clipboard refresh; initial copy before Shown with fake clipboard; cancellation; fractional zoom coordinates; antialiased pen pixels; stroke context reuse; undo/redo; live HSV picker preview and Cancel restoration; maximum ten-step history; histories cleared on editor close and never restored; no full-image history copy for a click without drawing. No desktop capture or real clipboard changes.");
 }
}
