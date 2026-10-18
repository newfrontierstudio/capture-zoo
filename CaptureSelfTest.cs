using System.Reflection;
namespace CaptureZoo;

internal static class CaptureSelfTest
{
    internal static int Run()
    {
        try{
            ApplicationConfiguration.Initialize();
            using var icon=AppIcon.Load();
            using var source=new Bitmap(160,100);
            using(var graphics=Graphics.FromImage(source))graphics.Clear(Color.White);
            using var editor=new EditorForm(source);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var image=(Bitmap)typeof(EditorForm).GetField("_image",flags)!.GetValue(editor)!;
            var undo=(List<Bitmap>)typeof(EditorForm).GetField("_undoHistory",flags)!.GetValue(editor)!;
            undo.Add(new Bitmap(image));image.SetPixel(30,30,Color.Black);
            typeof(EditorForm).GetMethod("RestoreUndo",flags)!.Invoke(editor,null);
            if(image.GetPixel(30,30).ToArgb()!=Color.White.ToArgb())throw new Exception("Undo did not restore the image");
            typeof(EditorForm).GetMethod("RestoreRedo",flags)!.Invoke(editor,null);
            if(image.GetPixel(30,30).ToArgb()!=Color.Black.ToArgb())throw new Exception("Redo did not restore the stroke");
            if(source.GetPixel(30,30).ToArgb()!=Color.White.ToArgb())throw new Exception("Editing changed the original image");
            if(!editor.Text.StartsWith("CaptureZoo"))throw new Exception("Application branding mismatch");
            using(var recovery=new EditorForm(source)){
                Exception? failure=null;
                recovery.Shown+=(_,_)=>recovery.BeginInvoke((Action)(()=>{
                    try{
                        recovery.Location=new Point(-100000,-100000);recovery.WindowState=FormWindowState.Minimized;
                        if(!TrayApplicationContext.PresentCaptureWindow(recovery)||recovery.WindowState==FormWindowState.Minimized||!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(recovery.Bounds)))throw new Exception("Hidden editor recovery failed");
                    }catch(Exception e){failure=e;}finally{recovery.CancelCapture();}
                }));
                if(recovery.ShowDialog()!=DialogResult.Cancel||recovery.FinalImage!=null)throw new Exception("Cancel must not confirm an image");
                if(failure!=null)throw failure;
            }
            using(var disposed=new Form()){disposed.Dispose();if(TrayApplicationContext.PresentCaptureWindow(disposed))throw new Exception("Disposed window must not be restored");}
            if(TrayApplicationContext.PresentCaptureWindow(null))throw new Exception("Missing window must not be restored");
            Console.WriteLine("PASS: icon, editor initialization, undo, redo, source image isolation. No screen capture or clipboard writes.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
