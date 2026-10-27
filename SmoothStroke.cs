using System.Drawing.Drawing2D;
namespace CaptureZoo;
// One-sample lookahead, with an immediate preview of the unfinished tail.
// At most four input points are retained; long strokes do not grow the render cost.
internal sealed class SmoothStroke
{
 internal readonly record struct Segment(PointF Start,PointF C1,PointF C2,PointF End)
 {
  internal void Draw(Graphics graphics,Pen pen)=>graphics.DrawBezier(pen,Start,C1,C2,End);
  internal RectangleF Bounds=>RectangleF.FromLTRB(new[]{Start.X,C1.X,C2.X,End.X}.Min(),new[]{Start.Y,C1.Y,C2.Y,End.Y}.Min(),new[]{Start.X,C1.X,C2.X,End.X}.Max(),new[]{Start.Y,C1.Y,C2.Y,End.Y}.Max());
 }
 // Maximum geometric correction, in image pixels. The editor scales this
 // from screen pixels so the feel stays consistent on large/zoomed captures.
 internal float ApproximationTolerance{get;set;}=2.25f;
 readonly List<PointF> points=new(4);
 readonly List<bool> stopped=new(4);
 long lastSample;
 internal void Start(PointF point,long? timestamp=null){points.Clear();stopped.Clear();points.Add(point);stopped.Add(true);lastSample=timestamp??System.Diagnostics.Stopwatch.GetTimestamp();}
 internal Segment? Add(PointF point,bool final=false,long? timestamp=null){
  long time=timestamp??System.Diagnostics.Stopwatch.GetTimestamp();
  if(points.Count==0){Start(point,time);return null;}
  if(!final){var previous=points[^1];float dx=point.X-previous.X,dy=point.Y-previous.Y,distance=MathF.Sqrt(dx*dx+dy*dy);if(distance<.5f)return null;float weight=distance<2?.3f:distance<8?.5f:distance<24?.8f:1;point=new(previous.X+dx*weight,previous.Y+dy*weight);}
  if(points[^1]==point)return null;
  // A pause followed by a new direction is intentional: keep that vertex
  // rather than rounding it as if it were high-speed mouse jitter.
  if(!final&&System.Diagnostics.Stopwatch.GetElapsedTime(lastSample,time).TotalMilliseconds>=120)stopped[^1]=true;
  lastSample=time;points.Add(point);stopped.Add(final);if(points.Count<3)return null;
  Segment segment=points.Count==3?Curve(points[0],points[0],points[1],points[2],stopped[0],stopped[1]):Curve(points[0],points[1],points[2],points[3],stopped[1],stopped[2]);
  if(points.Count==4){points.RemoveAt(0);stopped.RemoveAt(0);}return segment;
 }
 internal Segment? Preview=>points.Count<2?null:Curve(points.Count>=3?points[^3]:points[^2],points[^2],points[^1],points[^1],stopped[^2],true);
 internal void Clear(){points.Clear();stopped.Clear();}
 PointF ApproximateAnchor(PointF before,PointF current,PointF after)
 {
  float ix=current.X-before.X,iy=current.Y-before.Y,ox=after.X-current.X,oy=after.Y-current.Y;
  float incoming=MathF.Sqrt(ix*ix+iy*iy),outgoing=MathF.Sqrt(ox*ox+oy*oy);
  if(incoming<.001f||outgoing<.001f)return current;
  float cosine=(ix*ox+iy*oy)/(incoming*outgoing);
  // Leave stable arcs/straight lines and deliberate reversals alone. Replace
  // abrupt local direction changes with a bounded approximation of the trace.
  if(cosine>.819152f||cosine<-.965926f)return current;
  float cx=after.X-before.X,cy=after.Y-before.Y,chord=cx*cx+cy*cy;
  if(chord<.001f)return current;
  float along=Math.Clamp(((current.X-before.X)*cx+(current.Y-before.Y)*cy)/chord,0,1);
  float dx=(before.X+cx*along-current.X)*.4f,dy=(before.Y+cy*along-current.Y)*.4f;
  float length=MathF.Sqrt(dx*dx+dy*dy),limit=Math.Clamp(ApproximationTolerance,0,16);
  if(length>limit&&length>0){dx*=limit/length;dy*=limit/length;}
  return new(current.X+dx,current.Y+dy);
 }
 Segment Curve(PointF a,PointF b,PointF c,PointF d,bool keepStart,bool keepEnd)
 {
  // Chord-limited tangents avoid loops/long overshoot when speed changes abruptly.
  static PointF Control(PointF origin,PointF from,PointF to,float limit,float sign){float x=(to.X-from.X)/6,y=(to.Y-from.Y)/6,length=MathF.Sqrt(x*x+y*y);if(length>limit&&length>0){x*=limit/length;y*=limit/length;}return new(origin.X+sign*x,origin.Y+sign*y);}
  var start=keepStart?b:ApproximateAnchor(a,b,c);var end=keepEnd?c:ApproximateAnchor(b,c,d);
  float dx=end.X-start.X,dy=end.Y-start.Y,limit=MathF.Sqrt(dx*dx+dy*dy)/3;
  return new(start,keepStart?new(start.X+dx/3,start.Y+dy/3):Control(start,a,c,limit,1),keepEnd?new(end.X-dx/3,end.Y-dy/3):Control(end,b,d,limit,-1),end);
 }
}
