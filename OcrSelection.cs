namespace CaptureZoo;
internal static class OcrSelection
{
 internal static Rectangle? Bounds(Point start,Point end,Size canvas,Size image)
 {
  if(canvas.Width<=0||canvas.Height<=0||image.Width<=0||image.Height<=0)return null;
  float scale=Math.Min((float)canvas.Width/image.Width,(float)canvas.Height/image.Height),x=(canvas.Width-image.Width*scale)/2,y=(canvas.Height-image.Height*scale)/2;
  if(!new RectangleF(x,y,image.Width*scale,image.Height*scale).Contains(start))return null;
  float X(int v)=>Math.Clamp((v-x)/scale,0,image.Width);float Y(int v)=>Math.Clamp((v-y)/scale,0,image.Height);
  int left=(int)Math.Floor(Math.Min(X(start.X),X(end.X))),top=(int)Math.Floor(Math.Min(Y(start.Y),Y(end.Y))),right=(int)Math.Ceiling(Math.Max(X(start.X),X(end.X))),bottom=(int)Math.Ceiling(Math.Max(Y(start.Y),Y(end.Y)));
  var result=Rectangle.Intersect(new Rectangle(Point.Empty,image),Rectangle.FromLTRB(left,top,right,bottom));return result.Width>0&&result.Height>0?result:null;
 }
 internal static bool Dragged(Point start,Point end)=>Math.Abs(end.X-start.X)>=4||Math.Abs(end.Y-start.Y)>=4;
}
