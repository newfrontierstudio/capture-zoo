namespace CaptureZoo;
internal enum DirectGestureKind{Begin,Move,End,Cancel}
internal sealed record DirectGesture(DirectGestureKind Kind,long Id,Point Start,Point Current);
internal enum GestureMouse{Down,Move,Up}
internal readonly record struct GestureDecision(bool Suppress,bool ReplayClick,DirectGesture? Update);
internal sealed class RightDragGesture
{
 readonly object gate=new();int state;Point start,current;long serial;
 internal GestureDecision Process(GestureMouse kind,Point point,bool allowed=true)
 {
  lock(gate){
   if(kind==GestureMouse.Down){if(state!=0)return new(true,false,null);if(!allowed)return default;start=current=point;state=1;serial++;return new(true,false,null);}
   if(state==0)return default;
   if(kind==GestureMouse.Move){current=point;if(state==3)return default;if(state==1){if(Math.Abs(point.X-start.X)<6&&Math.Abs(point.Y-start.Y)<6)return default;state=2;return new(false,false,new(DirectGestureKind.Begin,serial,start,current));}return new(false,false,new(DirectGestureKind.Move,serial,start,current));}
   if(kind==GestureMouse.Up){int prior=state;state=0;current=point;return new(true,prior==1,prior==2?new(DirectGestureKind.End,serial,start,current):null);}return default;
  }
 }
 internal DirectGesture? Cancel(){lock(gate){if(state is 0 or 3)return null;bool selecting=state==2;state=3;return selecting?new(DirectGestureKind.Cancel,serial,start,current):null;}}
 internal bool Active{get{lock(gate)return state is 1 or 2;}}
}
