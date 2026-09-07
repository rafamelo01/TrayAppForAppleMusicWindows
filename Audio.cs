using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class AppleMusicVolume {
 public static float Clamp(float v) { return Math.Max(0, Math.Min(1,v)); }
 [MTAThread] public static int RunCommand(string[] args) {
  try { if(args.Length!=1 || (args[0]!="up" && args[0]!="down")) return 2;
   using(var mutex=new Mutex(false,"Local\\AppleMusicVolumeAdjustment")) {
    bool owned=false; try { try { owned=mutex.WaitOne(3000); } catch(AbandonedMutexException) { owned=true; }
     if(!owned) return 3; Inspect(args[0]=="up" ? .02f : -.02f, true); return 0;
    } finally { if(owned) mutex.ReleaseMutex(); }
   }
  } catch { return 1; }
 }
 static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
 static void Release(object o) { if(o!=null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
 public static string[] Inspect(float delta,bool change) {
  var results=new List<string>(); IMMDeviceEnumerator en=null; IMMDeviceCollection devices=null;
  try {
   en=(IMMDeviceEnumerator)new MMDeviceEnumerator(); Check(en.EnumAudioEndpoints(0,1,out devices)); uint n; Check(devices.GetCount(out n));
   for(uint d=0;d<n;d++) {
    IMMDevice device=null; object managerObj=null; IAudioSessionEnumerator sessions=null;
    var controls=new List<IAudioSessionControl2>(); var appleFlags=new List<bool>(); var pids=new List<uint>();
    float? activeLevel=null; float? fallbackLevel=null;
    try {
     Check(devices.Item(d,out device)); Guid iid=typeof(IAudioSessionManager2).GUID; Check(device.Activate(ref iid,23,IntPtr.Zero,out managerObj));
     Check(((IAudioSessionManager2)managerObj).GetSessionEnumerator(out sessions)); int count; Check(sessions.GetCount(out count));
     for(int i=0;i<count;i++) {
      IAudioSessionControl2 session; Check(sessions.GetSession(i,out session)); controls.Add(session);
      uint pid; Check(session.GetProcessId(out pid)); pids.Add(pid);
      bool apple=false;
      try { using(var p=Process.GetProcessById((int)pid)) {
       var name=p.ProcessName;
       if(String.Equals(name,"AppleMusic",StringComparison.OrdinalIgnoreCase) || String.Equals(name,"AMPLibraryAgent",StringComparison.OrdinalIgnoreCase)) {
        var dir=System.IO.Path.GetDirectoryName(p.MainModule.FileName);
        apple=System.IO.Path.GetFileName(dir).StartsWith("AppleInc.AppleMusicWin_",StringComparison.OrdinalIgnoreCase);
       }
      }} catch(ArgumentException) {} catch(System.ComponentModel.Win32Exception) {}
      appleFlags.Add(apple);
      if(apple) {
       float level; Check(((ISimpleAudioVolume)session).GetMasterVolume(out level));
       int state; Check(session.GetState(out state));
       if(!fallbackLevel.HasValue) fallbackLevel=level;
       if(state==1 && !activeLevel.HasValue) activeLevel=level;
      }
     }
     float target=Clamp((float)Math.Round((activeLevel ?? fallbackLevel ?? 0)+delta,4));
     for(int i=0;i<controls.Count;i++) {
      var volume=(ISimpleAudioVolume)controls[i];
      if(change && appleFlags[i]) { Guid context=Guid.Empty; Check(volume.SetMasterVolume(target,ref context)); }
      float after; Check(volume.GetMasterVolume(out after));
      results.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture,"device={0};session={1};pid={2};apple={3};volume={4:R}",d,i,pids[i],appleFlags[i],after));
     }
    } finally { foreach(var control in controls) Release(control); Release(sessions); Release(managerObj); Release(device); }
   }
  } finally { Release(devices); Release(en); }
  return results.ToArray();
 }
}
[ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator {}
[ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator {
 [PreserveSig] int EnumAudioEndpoints(int flow,uint mask,out IMMDeviceCollection devices);
}
[ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceCollection {
 [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index,out IMMDevice device);
}
[ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice {
 [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)] out object result);
}
[ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionManager2 {
 [PreserveSig] int GetAudioSessionControl(ref Guid g,uint flags,out IntPtr control);
 [PreserveSig] int GetSimpleAudioVolume(ref Guid g,uint flags,out IntPtr volume);
 [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
}
[ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionEnumerator {
 [PreserveSig] int GetCount(out int count); [PreserveSig] int GetSession(int index,out IAudioSessionControl2 session);
}
[ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionControl2 {
 [PreserveSig] int GetState(out int state);
 [PreserveSig] int GetDisplayName(out IntPtr name); [PreserveSig] int SetDisplayName(IntPtr name,ref Guid ctx);
 [PreserveSig] int GetIconPath(out IntPtr path); [PreserveSig] int SetIconPath(IntPtr path,ref Guid ctx);
 [PreserveSig] int GetGroupingParam(out Guid g); [PreserveSig] int SetGroupingParam(ref Guid g,ref Guid ctx);
 [PreserveSig] int RegisterAudioSessionNotification(IntPtr n); [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
 [PreserveSig] int GetSessionIdentifier(out IntPtr id); [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
 [PreserveSig] int GetProcessId(out uint pid);
}
[ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISimpleAudioVolume {
 [PreserveSig] int SetMasterVolume(float level,ref Guid context); [PreserveSig] int GetMasterVolume(out float level);
 [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context); [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
}