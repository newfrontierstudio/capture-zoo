using System.Runtime.InteropServices;
namespace CaptureZoo;
// Hooks have their own message loop so capture/model work never delays input.
internal sealed class GlobalOcrGesture:IDisposable
{
 delegate IntPtr Hook(int code,IntPtr wParam,IntPtr lParam);
 [StructLayout(LayoutKind.Sequential)]struct MouseData{public Point Point;public uint MouseDataValue,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct KeyData{public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct NativeMessage{public IntPtr Hwnd;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public Point Point;public uint Private;}
 [StructLayout(LayoutKind.Sequential)]struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct InputUnion{[FieldOffset(0)]public MouseInput Mouse;}
 [StructLayout(LayoutKind.Sequential)]struct Input{public uint Type;public InputUnion Data;}
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,Hook hook,IntPtr module,uint thread);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? name);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern int GetMessage(out NativeMessage message,IntPtr hwnd,uint min,uint max);
 [DllImport("user32.dll")]static extern bool PeekMessage(out NativeMessage message,IntPtr hwnd,uint min,uint max,uint remove);
 [DllImport("user32.dll")]static extern bool TranslateMessage(ref NativeMessage message);
 [DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref NativeMessage message);
 [DllImport("user32.dll")]static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 readonly RightDragGesture gesture=new();readonly Action<DirectGesture> publish;readonly Hook mouseCallback,keyCallback;
 readonly ManualResetEventSlim ready=new();readonly Thread? thread;readonly Mutex primary;readonly bool ownsMutex;
 IntPtr mouseHook,keyHook;uint threadId;long lastMove;volatile bool disposed;
 internal volatile bool Enabled;internal volatile bool Suspended;
 internal bool Registered{get;private set;}internal bool Secondary=>!ownsMutex;
 internal GlobalOcrGesture(Action<DirectGesture> publish)
 {
  this.publish=publish;mouseCallback=Mouse;keyCallback=Keyboard;primary=new Mutex(true,"Local\\CaptureZoo-DirectOCR-"+Environment.UserName,out ownsMutex);
  if(!ownsMutex)return;thread=new Thread(Loop){IsBackground=true,Name="CaptureZoo OCR mouse gesture"};thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!ready.Wait(3000))AppLog.Info("OCR gesture registration timed out.");
 }
 void Loop(){try{threadId=GetCurrentThreadId();PeekMessage(out _,IntPtr.Zero,0,0,0);var module=GetModuleHandle(null);mouseHook=SetWindowsHookEx(14,mouseCallback,module,0);keyHook=SetWindowsHookEx(13,keyCallback,module,0);Registered=mouseHook!=IntPtr.Zero&&keyHook!=IntPtr.Zero;ready.Set();if(!Registered){AppLog.Info("OCR gesture hooks unavailable: "+Marshal.GetLastWin32Error());return;}while(GetMessage(out var msg,IntPtr.Zero,0,0)>0){TranslateMessage(ref msg);DispatchMessage(ref msg);}}catch(Exception error){AppLog.Error(error);}finally{if(mouseHook!=IntPtr.Zero)UnhookWindowsHookEx(mouseHook);if(keyHook!=IntPtr.Zero)UnhookWindowsHookEx(keyHook);Registered=false;ready.Set();}}
 IntPtr Mouse(int code,IntPtr wParam,IntPtr lParam)
 {
  if(code<0||disposed||!Registered)return CallNextHookEx(mouseHook,code,wParam,lParam);
  try{uint message=(uint)wParam.ToInt64();if(message is not(0x0204 or 0x0205 or 0x0200))return CallNextHookEx(mouseHook,code,wParam,lParam);
   var data=Marshal.PtrToStructure<MouseData>(lParam);if((data.Flags&1)!=0)return CallNextHookEx(mouseHook,code,wParam,lParam);
   bool allowed=Enabled&&!Suspended;if(message==0x0204&&allowed){GetWindowThreadProcessId(WindowFromPoint(data.Point),out uint process);allowed=process!=(uint)Environment.ProcessId;}
   var decision=gesture.Process(message==0x0204?GestureMouse.Down:message==0x0205?GestureMouse.Up:GestureMouse.Move,data.Point,allowed);
   if(decision.Update is {} update){long now=System.Diagnostics.Stopwatch.GetTimestamp();if(update.Kind!=DirectGestureKind.Move||System.Diagnostics.Stopwatch.GetElapsedTime(lastMove,now).TotalMilliseconds>=8){lastMove=now;publish(update);}}
   if(decision.ReplayClick){var inputs=new[]{new Input{Data=new InputUnion{Mouse=new MouseInput{Flags=0x0008}}},new Input{Data=new InputUnion{Mouse=new MouseInput{Flags=0x0010}}}};if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)AppLog.Info("Right-click replay failed: "+Marshal.GetLastWin32Error());}
   return decision.Suppress?new IntPtr(1):CallNextHookEx(mouseHook,code,wParam,lParam);
  }catch(Exception error){AppLog.Error(error);return CallNextHookEx(mouseHook,code,wParam,lParam);}
 }
 IntPtr Keyboard(int code,IntPtr wParam,IntPtr lParam)
 {
  if(code>=0&&!disposed&&(wParam.ToInt64() is 0x0100 or 0x0104)&&gesture.Active){var data=Marshal.PtrToStructure<KeyData>(lParam);if(data.Key==27){Cancel();return new IntPtr(1);}}
  return CallNextHookEx(keyHook,code,wParam,lParam);
 }
 internal void Cancel(){var update=gesture.Cancel();if(update!=null&&!disposed)publish(update);}
 public void Dispose(){if(disposed)return;Cancel();disposed=true;if(threadId!=0)PostThreadMessage(threadId,0x0012,UIntPtr.Zero,IntPtr.Zero);thread?.Join(2000);if(ownsMutex)primary.ReleaseMutex();primary.Dispose();}
}
