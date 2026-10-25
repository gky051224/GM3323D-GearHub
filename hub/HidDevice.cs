// Enumeration and feature channel ported from the upstream MIT Win32 HID implementation.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GearHub {
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public int Size; public ushort Vid,Pid,Version; }
        [StructLayout(LayoutKind.Sequential)] internal struct Caps {
            public ushort Usage,Page,Input,Output,Feature;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
            public ushort Link,InButtons,InValues,InIndices,OutButtons,OutValues,OutIndices,FeatureButtons,FeatureValues,FeatureIndices;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Overlapped { public IntPtr Internal,InternalHigh; public uint Offset,OffsetHigh; public IntPtr Event; }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle h,ref Attributes a);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetProductString(SafeFileHandle h,byte[] text,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr data);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data,out Caps caps);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle h,byte[] data,int length);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetFeature(SafeFileHandle h,byte[] data,int length);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr hwnd,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr list,IntPtr dev,ref Guid guid,uint index,ref InterfaceData data);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr list,ref InterfaceData data,IntPtr detail,int size,out int required,IntPtr dev);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool ReadFile(SafeFileHandle h,IntPtr buffer,int count,IntPtr read,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr ov,out int read,bool wait);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CancelIoEx(SafeFileHandle h,IntPtr ov);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] internal static extern IntPtr OpenThread(uint rights,bool inherit,uint id);
        [DllImport("kernel32.dll")] internal static extern bool CancelSynchronousIo(IntPtr thread);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);

        internal static List<DeviceInfo> Find() {
            var result = new List<DeviceInfo>(); Guid guid; HidD_GetHidGuid(out guid);
            IntPtr list = SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);
            if (list==new IntPtr(-1)) return result;
            try {
                for (uint i=0;;i++) {
                    var item = new InterfaceData(); item.Size=Marshal.SizeOf(typeof(InterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(list,IntPtr.Zero,ref guid,i,ref item)) break;
                    int size; SetupDiGetDeviceInterfaceDetail(list,ref item,IntPtr.Zero,0,out size,IntPtr.Zero);
                    if (size<8) continue;
                    IntPtr detail=Marshal.AllocHGlobal(size);
                    try {
                        Marshal.WriteInt32(detail,IntPtr.Size==8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(list,ref item,detail,size,out size,IntPtr.Zero)) continue;
                        string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                        using (var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                            if (h.IsInvalid) continue;
                            var a=new Attributes();a.Size=Marshal.SizeOf(typeof(Attributes));
                            if (!HidD_GetAttributes(h,ref a) || a.Vid!=0x1D57 || a.Pid!=0xFA60) continue;
                            byte[] text=new byte[256];
                            if (!HidD_GetProductString(h,text,text.Length)) continue;
                            string product=System.Text.Encoding.Unicode.GetString(text).TrimEnd('\0');
                            // This VID/PID is shared with unrelated mice; require the GM3323D product too.
                            if (product.IndexOf("GM3323D",StringComparison.OrdinalIgnoreCase)<0) continue;
                            IntPtr ppd;
                            if (!HidD_GetPreparsedData(h,out ppd)) continue;
                            try { Caps caps;if (HidP_GetCaps(ppd,out caps)>=0) result.Add(new DeviceInfo(path,product,caps)); }
                            finally { HidD_FreePreparsedData(ppd); }
                        }
                    } finally { Marshal.FreeHGlobal(detail); }
                }
            } finally { SetupDiDestroyDeviceInfoList(list); }
            return result;
        }
    }

    internal sealed class DeviceInfo {
        public string Path,Product; public Native.Caps Caps;
        public DeviceInfo(string path,string product,Native.Caps caps) {Path=path;Product=product;Caps=caps;}
    }

    internal sealed class FeatureSession : IFeatureSession {
        readonly SafeFileHandle handle; readonly IntPtr thread;
        public FeatureSession() {
            DeviceInfo target=Native.Find().Find(d=>d.Caps.Page==0xB && d.Caps.Usage==0 && d.Caps.Feature==132);
            if (target==null) throw new InvalidOperationException("未找到 GM3323D 灯光接口，请插入无线接收器。");
            handle=Native.CreateFile(target.Path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if (handle.IsInvalid) throw new InvalidOperationException("灯光接口暂不可用，请关闭原厂设置窗口后重试。");
            thread=Native.OpenThread(1,false,Native.GetCurrentThreadId());
        }
        public byte[] Read() {var bytes=new byte[132];bytes[0]=4;return Native.HidD_GetFeature(handle,bytes,132)?bytes:null;}
        public bool Write(byte[] bytes) {return Native.HidD_SetFeature(handle,bytes,bytes.Length);}
        public void Cancel() {if (thread!=IntPtr.Zero) Native.CancelSynchronousIo(thread);}
        public void Dispose() {handle.Dispose();if(thread!=IntPtr.Zero)Native.CloseHandle(thread);}
    }

    internal sealed class BatteryMonitor : IDisposable {
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        public event Action<BatterySample,string> Updated;
        public void Start() {new Thread(Work) {IsBackground=true,Name="GM Gear battery"}.Start();}
        void Publish(BatterySample sample,string detail) {var handler=Updated;if(handler!=null)handler(sample,detail);}
        void Work() {
            while (!stop.IsCancellationRequested) {
                try {
                    var device=Native.Find().Find(d=>d.Caps.Page==0xA && d.Caps.Usage==0 && d.Caps.Input==5);
                    if(device==null) {Publish(new BatterySample(),"未检测到接收器");stop.Token.WaitHandle.WaitOne(2000);continue;}
                    using(var handle=Native.CreateFile(device.Path,0x80000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
                        if(handle.IsInvalid) throw new InvalidOperationException("接收器暂不可读");
                        DateTime last=DateTime.UtcNow; bool timeoutShown=false;
                        Publish(new BatterySample {Online=true},"等待鼠标电量回复");
                        while(!stop.IsCancellationRequested) {
                            byte[] packet=ReadInput(handle,stop.Token);
                            if(packet==null) {
                                if(!timeoutShown && (DateTime.UtcNow-last).TotalSeconds>12) {
                                    Publish(new BatterySample {Online=true},"请移动鼠标唤醒");timeoutShown=true;
                                }
                                continue;
                            }
                            BatterySample sample=BatterySample.Parse(packet);
                            if(!sample.Recognized)continue;
                            last=DateTime.UtcNow;timeoutShown=false;
                            Publish(sample,sample.Online ? (sample.Level.HasValue?"无线连接正常":"等待有效电量") : "鼠标未连接或已休眠");
                        }
                    }
                } catch(Exception) {if(!stop.IsCancellationRequested)Publish(new BatterySample(),"接收器已断开，等待重连");stop.Token.WaitHandle.WaitOne(2000);}
            }
        }
        static byte[] ReadInput(SafeFileHandle handle,CancellationToken cancel) {
            using(var signal=new ManualResetEvent(false)) {
                var ov=new Native.Overlapped();ov.Event=signal.SafeWaitHandle.DangerousGetHandle();
                IntPtr state=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Overlapped)));
                IntPtr buffer=Marshal.AllocHGlobal(5);
                try {
                    Marshal.StructureToPtr(ov,state,false);
                    bool ok=Native.ReadFile(handle,buffer,5,IntPtr.Zero,state);
                    int error=Marshal.GetLastWin32Error();
                    if(!ok && error!=997)throw new InvalidOperationException("读取中断");
                    bool timedOut=WaitHandle.WaitAny(new WaitHandle[]{signal,cancel.WaitHandle},1000)!=0;
                    if(timedOut)Native.CancelIoEx(handle,state);
                    int count;ok=Native.GetOverlappedResult(handle,state,out count,true);
                    if(timedOut)return null;
                    if(!ok)throw new InvalidOperationException("读取中断");
                    var bytes=new byte[count];Marshal.Copy(buffer,bytes,0,count);return bytes;
                } finally {Marshal.FreeHGlobal(state);Marshal.FreeHGlobal(buffer);}
            }
        }
        public void Dispose() {stop.Cancel();}
    }
}
