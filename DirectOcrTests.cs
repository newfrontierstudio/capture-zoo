namespace CaptureZoo;
internal static class DirectOcrTests{
 internal static int Run(){try{
 void Check(bool ok){if(!ok)throw new Exception("Direct OCR check failed");}
 var state=new RightDragGesture();Check(state.Process(GestureMouse.Down,new(1,1)).Suppress);var click=state.Process(GestureMouse.Up,new(1,1));Check(click.ReplayClick&&click.Update==null);
 state.Process(GestureMouse.Down,new(1,1));Check(state.Process(GestureMouse.Move,new(20,20)).Update?.Kind==DirectGestureKind.Begin);var end=state.Process(GestureMouse.Up,new(50,40));Check(end.Suppress&&!end.ReplayClick&&end.Update?.Kind==DirectGestureKind.End);
 state.Process(GestureMouse.Down,new(1,1));state.Process(GestureMouse.Move,new(20,20));Check(state.Cancel()?.Kind==DirectGestureKind.Cancel);Check(state.Process(GestureMouse.Up,new(40,40)).Update==null);
 Check(!state.Process(GestureMouse.Down,new(1,1),false).Suppress);
 Check(DirectOcrController.Crop(new(-90,20),new(-10,70),new(-100,0,200,100))==new Rectangle(10,20,80,50));
 using var hook=new GlobalOcrGesture(_=>{});Check(hook.Registered||hook.Secondary);
 File.WriteAllText("direct-ocr-tests.txt","PASS: normal click, drag, cancel, disabled, negative monitor crop, native hook registration");return 0;
 }catch(Exception e){File.WriteAllText("direct-ocr-tests.txt",e.ToString());return 1;}}
}
