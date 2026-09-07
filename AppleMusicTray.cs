using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class TrayProgram {
 internal const string Prefix="Local\\Rafael.AppleMusicHotkeys.";
 internal static readonly string Dir=AppDomain.CurrentDomain.BaseDirectory;
 internal static readonly string[] Commands={"show","hide","exit","volume-up","volume-down","toggle-music","show-music","hide-music"};
 [STAThread] public static int Main(string[] args) {
  string command=args.Length==0 ? "show" : args[0].TrimStart('-');
  bool fresh;
  using(var mutex=new Mutex(true,Prefix+"Instance",out fresh)) {
   if(!fresh) {
    if(command=="startup") return 0;
    int commandId=Array.IndexOf(Commands,command);
    if(commandId<0) return 2;
    for(int attempt=0;attempt<20;attempt++) {
     try { using(var ev=EventWaitHandle.OpenExisting(Prefix+command)) ev.Set(); return 0; }
     catch(WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
    }
    return 3;
   }
   try {
    if(command=="exit") return 0;
    if(command!="show" && command!="hide" && command!="startup") return 2;
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    using(var context=new MusicTray(command)) Application.Run(context);
    return 0;
   } catch(Exception ex) {
    File.WriteAllText(Path.Combine(Dir,"startup-error.txt"),DateTime.UtcNow.ToString("o")+" "+ex.GetType().Name+" "+ex.HResult.ToString("X"));
    MessageBox.Show("Não foi possível iniciar os atalhos do Apple Music. Outro app pode estar usando as mesmas teclas.","Apple Music Hotkeys",MessageBoxButtons.OK,MessageBoxIcon.Error);
    return 1;
   } finally { mutex.ReleaseMutex(); }
  }
 }
}
sealed class MusicTray : ApplicationContext {
 internal const int WM_COMMAND=0x8001;
 const string RunPath="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
 const string RunName="AppleMusicHotkeys";
 readonly HotkeyWindow window;
 readonly NotifyIcon tray;
 readonly ToolStripMenuItem startup;
 readonly ToolStripMenuItem hideOnMinimize;
 readonly System.Windows.Forms.Timer musicWindowTimer;
 readonly List<EventWaitHandle> events=new List<EventWaitHandle>();
 readonly BlockingCollection<int> work=new BlockingCollection<int>(8);
 readonly Thread worker;
 readonly Thread commandThread;
 readonly Icon icon;
 bool hideOnMinimizeEnabled;
 volatile bool closing=false;
 string lastAction="started";
 string lastError="";
 internal int Registered=0;
 internal MusicTray(string command) {
  window=new HotkeyWindow(this);
  tray=new NotifyIcon(); icon=CreateIcon(); tray.Icon=icon; tray.Text="Apple Music · F2 / Ctrl+Alt+↑↓ / Ctrl+Alt+M";
  var menu=new ContextMenuStrip();
  menu.Items.Add(new ToolStripMenuItem("Apple Music Hotkeys") { Enabled=false });
  menu.Items.Add("Mostrar Apple Music",null,(s,e)=>ShowAppleMusic());
  menu.Items.Add("Ocultar Apple Music",null,(s,e)=>HideAppleMusic());
  menu.Items.Add("Alternar janela (Ctrl + Alt + M)",null,(s,e)=>ToggleAppleMusic());
  hideOnMinimizeEnabled=LoadHideOnMinimize();
  hideOnMinimize=new ToolStripMenuItem("Ocultar Apple Music ao minimizar");
  hideOnMinimize.Checked=hideOnMinimizeEnabled;
  hideOnMinimize.Click+=(s,e)=> {
   hideOnMinimizeEnabled=!hideOnMinimizeEnabled;
   SaveHideOnMinimize(); hideOnMinimize.Checked=hideOnMinimizeEnabled;
   musicWindowTimer.Enabled=hideOnMinimizeEnabled; WriteStatus();
  };
  menu.Items.Add(hideOnMinimize);
  menu.Items.Add(new ToolStripSeparator());
  menu.Items.Add("Reproduzir / pausar (F2)",null,(s,e)=>Action(1));
  menu.Items.Add("Aumentar volume +2%",null,(s,e)=>Action(2));
  menu.Items.Add("Diminuir volume -2%",null,(s,e)=>Action(3));
  menu.Items.Add(new ToolStripSeparator());
  startup=new ToolStripMenuItem("Iniciar com o Windows"); startup.Checked=StartupEnabled();
  startup.Click+=(s,e)=> { try { SetStartup(!StartupEnabled()); startup.Checked=StartupEnabled(); WriteStatus(); } catch(Exception ex){ Fail(ex); } };
  menu.Items.Add(startup); menu.Opening+=(s,e)=>startup.Checked=StartupEnabled();
  menu.Items.Add("Ocultar ícone (manter atalhos)",null,(s,e)=>SetVisible(false));
  menu.Items.Add("Como mostrar o ícone novamente",null,(s,e)=>MessageBox.Show("Abra Apple Music Hotkeys pelo menu Iniciar para mostrar o ícone novamente. Os atalhos continuam funcionando enquanto o ícone está oculto.","Apple Music Hotkeys"));
  menu.Items.Add(new ToolStripSeparator());
  menu.Items.Add("Sair (desativar atalhos)",null,(s,e)=>ExitThread());
  tray.ContextMenuStrip=menu;
  tray.DoubleClick+=(s,e)=>ToggleAppleMusic();
  int[] keys={0x71,0x26,0x28,0x4D};
  for(int i=0;i<4;i++) {
   if(!Native.RegisterHotKey(window.Handle,i+1,(uint)(0x4000+(i==0 ? 0 : 3)),(uint)keys[i])) {
    for(int j=1;j<=Registered;j++) Native.UnregisterHotKey(window.Handle,j);
    tray.Dispose(); icon.Dispose(); window.DestroyHandle();
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   }
   Registered++;
  }
  for(int i=0;i<TrayProgram.Commands.Length;i++) events.Add(new EventWaitHandle(false,EventResetMode.AutoReset,TrayProgram.Prefix+TrayProgram.Commands[i]));
  worker=new Thread(WorkerLoop); worker.IsBackground=true; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
  commandThread=new Thread(CommandServerLoop); commandThread.IsBackground=true; commandThread.Start();
  musicWindowTimer=new System.Windows.Forms.Timer(); musicWindowTimer.Interval=1000;
  musicWindowTimer.Tick+=(s,e)=> {
   if(AppleMusicWindow.IsMinimized()) HideAppleMusic();
  };
  musicWindowTimer.Enabled=hideOnMinimizeEnabled;
  bool savedHidden=File.Exists(Path.Combine(TrayProgram.Dir,"hidden.txt")) && File.ReadAllText(Path.Combine(TrayProgram.Dir,"hidden.txt")).Trim()=="1";
  SetVisible(command=="show" || (command=="startup" && !savedHidden));
 }
 internal void Command(int id) {
  if(closing)return;
  if(id==0) SetVisible(true);
  else if(id==1) SetVisible(false);
  else if(id==2) ExitThread();
  else if(id==3) Action(2);
  else if(id==4) Action(3);
  else if(id==5) ToggleAppleMusic();
  else if(id==6) ShowAppleMusic();
  else if(id==7) HideAppleMusic();
  else if(id==8) WriteStatus();
 }
 internal void Action(int id) {
  if(closing)return;
  if(id==1) {
   var input=new Native.INPUT[2];
   input[0].type=1; input[0].data.ki.wVk=0xB3;
   input[1].type=1; input[1].data.ki.wVk=0xB3; input[1].data.ki.dwFlags=2;
   if(Native.SendInput(2,input,Marshal.SizeOf(typeof(Native.INPUT)))!=2) { lastError="media-input-failed"; }
   lastAction="play-pause"; WriteStatus();
  } else if(id==2 || id==3) work.TryAdd(id);
  else if(id==4) ToggleAppleMusic();
 }
 void WorkerLoop() {
  foreach(int id in work.GetConsumingEnumerable()) {
   try {
    int code=AppleMusicVolume.RunCommand(new[]{id==2 ? "up" : "down"});
    lastAction=id==2 ? "volume-up" : "volume-down"; lastError=code==0 ? "" : "audio-error-"+code;
   } catch(Exception ex) { lastError=ex.GetType().Name; }
   Native.PostMessage(window.Handle,WM_COMMAND,new IntPtr(8),IntPtr.Zero);
  }
 }
 void CommandServerLoop() {
  var handles=events.ToArray();
  while(!closing) {
   int command=WaitHandle.WaitAny(handles);
   if(!closing && command>=0 && command<TrayProgram.Commands.Length) Native.PostMessage(window.Handle,WM_COMMAND,new IntPtr(command),IntPtr.Zero);
  }
 }
 void ShowAppleMusic() {
  try {
   bool opened=AppleMusicWindow.ShowOrLaunch();
   lastAction=opened ? "show-music" : "launch-music"; lastError="";
  } catch(Exception ex) { lastError=ex.GetType().Name; }
  WriteStatus();
 }
 void HideAppleMusic() {
  try {
   AppleMusicWindow.Hide(); lastAction="hide-music"; lastError="";
  } catch(Exception ex) { lastError=ex.GetType().Name; }
  WriteStatus();
 }
 void ToggleAppleMusic() {
  if(AppleMusicWindow.HasVisibleWindow()) HideAppleMusic(); else ShowAppleMusic();
 }
 static string HideOnMinimizePath { get { return Path.Combine(TrayProgram.Dir,"hide-on-minimize.txt"); } }
 static bool LoadHideOnMinimize() { return File.Exists(HideOnMinimizePath) && File.ReadAllText(HideOnMinimizePath).Trim()=="1"; }
 void SaveHideOnMinimize() { File.WriteAllText(HideOnMinimizePath,hideOnMinimizeEnabled ? "1" : "0"); }
 void SetVisible(bool visible) {
  tray.Visible=visible;
  File.WriteAllText(Path.Combine(TrayProgram.Dir,"hidden.txt"),visible ? "0" : "1");
  WriteStatus();
 }
 static bool StartupEnabled() { using(var k=Registry.CurrentUser.OpenSubKey(RunPath)) return k!=null && k.GetValue(RunName)!=null; }
 static void SetStartup(bool enabled) {
  using(var k=Registry.CurrentUser.CreateSubKey(RunPath)) {
   if(enabled) k.SetValue(RunName,"\""+Application.ExecutablePath+"\" --startup"); else k.DeleteValue(RunName,false);
  }
 }
 void Fail(Exception ex) { lastError=ex.GetType().Name; WriteStatus(); tray.ShowBalloonTip(4000,"Apple Music Hotkeys","Não foi possível aplicar a opção.",ToolTipIcon.Warning); }
 void WriteStatus() {
  try { File.WriteAllText(Path.Combine(TrayProgram.Dir,"status.txt"),"pid="+Process.GetCurrentProcess().Id+"\nregistered="+Registered+"\ntrayVisible="+tray.Visible+"\nstartup="+StartupEnabled()+"\nhideMusicOnMinimize="+hideOnMinimizeEnabled+"\nmusicWindowVisible="+AppleMusicWindow.HasVisibleWindow()+"\nlastAction="+lastAction+"\nlastError="+lastError+"\nupdatedUtc="+DateTime.UtcNow.ToString("o")); } catch {}
 }
 static Icon CreateIcon() {
  using(var bitmap=new Bitmap(32,32)) {
   using(var g=Graphics.FromImage(bitmap)) {
    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using(var brush=new SolidBrush(Color.FromArgb(230,45,90))) g.FillEllipse(brush,1,1,30,30);
    using(var pen=new Pen(Color.White,3)) { g.DrawLine(pen,13,22,13,10); g.DrawLine(pen,13,10,23,8); g.DrawLine(pen,23,8,23,20); }
    g.FillEllipse(Brushes.White,7,20,7,5); g.FillEllipse(Brushes.White,17,18,7,5);
   }
   IntPtr h=bitmap.GetHicon(); try { using(var borrowed=Icon.FromHandle(h)) return (Icon)borrowed.Clone(); } finally { Native.DestroyIcon(h); }
  }
 }
 protected override void ExitThreadCore() {
  if(closing)return; closing=true;
  musicWindowTimer.Stop(); musicWindowTimer.Dispose();
  for(int i=1;i<=Registered;i++) Native.UnregisterHotKey(window.Handle,i);
  Registered=0; if(events.Count>0) events[0].Set(); commandThread.Join(1000); work.CompleteAdding(); worker.Join(3000);
  tray.Visible=false; lastAction="stopped"; WriteStatus();
  foreach(var ev in events) ev.Dispose();
  tray.Dispose(); icon.Dispose(); window.DestroyHandle();
  base.ExitThreadCore();
 }
}

static class AppleMusicWindow {
 const int SW_HIDE=0;
 const int SW_RESTORE=9;
 static readonly object cacheLock=new object();
 static readonly List<IntPtr> hiddenWindows=new List<IntPtr>();
 static List<IntPtr> cachedWindows=new List<IntPtr>();
 static DateTime cacheExpiresUtc=DateTime.MinValue;
 static List<IntPtr> FindWindows(bool forceRefresh=false) {
  lock(cacheLock) {
   cachedWindows.RemoveAll(hwnd=>!Native.IsWindow(hwnd));
   if(!forceRefresh && DateTime.UtcNow<cacheExpiresUtc) return new List<IntPtr>(cachedWindows);
   var pids=new HashSet<uint>();
   foreach(var process in Process.GetProcessesByName("AppleMusic")) {
    try { pids.Add((uint)process.Id); } finally { process.Dispose(); }
   }
  var result=new List<IntPtr>();
  Native.EnumWindows((hwnd,param)=> {
   uint pid; Native.GetWindowThreadProcessId(hwnd,out pid);
   if(pids.Contains(pid) && Native.GetWindow(hwnd,4)==IntPtr.Zero && Native.GetWindowTextLength(hwnd)>0) result.Add(hwnd);
   return true;
  },IntPtr.Zero);
   cachedWindows=result; cacheExpiresUtc=DateTime.UtcNow.AddSeconds(1);
   return new List<IntPtr>(result);
  }
 }
 internal static bool HasVisibleWindow() {
  foreach(var hwnd in FindWindows()) if(Native.IsWindowVisible(hwnd)) return true;
  return false;
 }
 internal static bool IsMinimized() {
  foreach(var hwnd in FindWindows()) if(Native.IsWindowVisible(hwnd) && Native.IsIconic(hwnd)) return true;
  return false;
 }
 internal static bool Hide() {
  bool changed=false;
  foreach(var hwnd in FindWindows()) if(Native.IsWindowVisible(hwnd)) {
   if(!hiddenWindows.Contains(hwnd)) hiddenWindows.Add(hwnd);
   Native.ShowWindow(hwnd,SW_HIDE); changed=true;
  }
  if(changed) cacheExpiresUtc=DateTime.UtcNow.AddSeconds(1);
  return changed;
 }
 internal static bool ShowOrLaunch() {
  IntPtr target=IntPtr.Zero;
  for(int i=hiddenWindows.Count-1;i>=0;i--) {
   if(!Native.IsWindow(hiddenWindows[i])) hiddenWindows.RemoveAt(i);
   else if(target==IntPtr.Zero) target=hiddenWindows[i];
  }
  if(target==IntPtr.Zero) {
   foreach(var hwnd in FindWindows(true)) { target=hwnd; if(Native.IsWindowVisible(hwnd)) break; }
  }
  if(target!=IntPtr.Zero) {
   Native.ShowWindow(target,SW_RESTORE); Native.SetForegroundWindow(target); hiddenWindows.Remove(target); return true;
  }
  Process.Start(new ProcessStartInfo("explorer.exe","shell:AppsFolder\\AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") { UseShellExecute=true });
  return false;
 }
}
sealed class HotkeyWindow : NativeWindow {
 readonly MusicTray app;
 internal HotkeyWindow(MusicTray app) { this.app=app; CreateHandle(new CreateParams { Caption="AppleMusicHotkeys.MessageWindow",Parent=new IntPtr(-3) }); }
 protected override void WndProc(ref Message m) {
  if(m.Msg==0x312) app.Action(m.WParam.ToInt32());
  else if(m.Msg==MusicTray.WM_COMMAND) app.Command(m.WParam.ToInt32());
  base.WndProc(ref m);
 }
}
static class Native {
 internal delegate bool EnumWindowsProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
 [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback,IntPtr param);
 [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowTextLength(IntPtr hwnd);
 [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,INPUT[] input,int size);
 [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal InputUnion data; }
 [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] internal KEYBDINPUT ki; [FieldOffset(0)] internal MOUSEINPUT mi; }
 [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort wVk,wScan; internal uint dwFlags,time; internal UIntPtr dwExtraInfo; }
 [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int dx,dy; internal uint mouseData,dwFlags,time; internal UIntPtr dwExtraInfo; }
}
