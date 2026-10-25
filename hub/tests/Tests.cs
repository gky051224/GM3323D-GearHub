using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text;

namespace GearHub {
    // Integration-only background listener: accept keyboard input solely from the GM receiver.
    internal sealed class RawKeyboardProbe : IDisposable {
        [StructLayout(LayoutKind.Sequential)]struct Device{public ushort Page,Usage;public uint Flags;public IntPtr Window;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
        [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,IntPtr buffer,ref uint size,uint header);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder name,ref uint size);
        readonly HwndSource source;readonly HwndSourceHook hook;
        public event Action<ushort,bool> Received;
        public RawKeyboardProbe(Window window){
            IntPtr handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);hook=Input;source.AddHook(hook);
            if(!RegisterRawInputDevices(new[]{new Device{Page=1,Usage=6,Flags=0x100,Window=handle},new Device{Page=1,Usage=2,Flags=0x100,Window=handle}},2,(uint)Marshal.SizeOf(typeof(Device))))throw new Exception("Raw input listener could not register");
        }
        IntPtr Input(IntPtr h,int message,IntPtr w,IntPtr input,ref bool handled){
            if(message!=0xFF)return IntPtr.Zero;uint size=0,header=(uint)(8+IntPtr.Size*2);
            GetRawInputData(input,0x10000003,IntPtr.Zero,ref size,header);if(size<header+16||size>4096)return IntPtr.Zero;
            IntPtr bytes=Marshal.AllocHGlobal((int)size);
            try{
                if(GetRawInputData(input,0x10000003,bytes,ref size,header)==UInt32.MaxValue)return IntPtr.Zero;
                IntPtr device=Marshal.ReadIntPtr(bytes,8);uint chars=0;GetRawInputDeviceInfo(device,0x20000007,null,ref chars);
                if(chars==0||chars>2048)return IntPtr.Zero;var path=new StringBuilder((int)chars+1);GetRawInputDeviceInfo(device,0x20000007,path,ref chars);
                string name=path.ToString();if(name.IndexOf("VID_1D57&PID_FA60",StringComparison.OrdinalIgnoreCase)<0)return IntPtr.Zero;
                if(Marshal.ReadInt32(bytes)==0){ushort buttons=(ushort)Marshal.ReadInt16(bytes,(int)header+4);if((buttons&0x3C0)!=0)Console.WriteLine("GM RECEIVER SIDE BUTTON FLAGS 0x"+buttons.ToString("X4"));return IntPtr.Zero;}
                if(Marshal.ReadInt32(bytes)!=1)return IntPtr.Zero;
                ushort key=(ushort)Marshal.ReadInt16(bytes,(int)header+6),flags=(ushort)Marshal.ReadInt16(bytes,(int)header+2);
                var received=Received;if(received!=null)received(key,(flags&1)==0);
            }finally{Marshal.FreeHGlobal(bytes);}return IntPtr.Zero;
        }
        public void Dispose(){RegisterRawInputDevices(new[]{new Device{Page=1,Usage=6,Flags=1,Window=IntPtr.Zero},new Device{Page=1,Usage=2,Flags=1,Window=IntPtr.Zero}},2,(uint)Marshal.SizeOf(typeof(Device)));source.RemoveHook(hook);}
    }
    internal sealed class FakeSession : IFeatureSession {
        public byte[] Sent; public int Writes;
        public readonly System.Collections.Generic.List<byte[]> History=new System.Collections.Generic.List<byte[]>();
        public Action OnWrite;
        public bool Accepted=true;
        public bool Write(byte[] b){Writes++;Sent=b;History.Add((byte[])b.Clone());if(OnWrite!=null)OnWrite();return Accepted;}
        public void Cancel(){}public void Dispose(){}
    }
    internal static class Tests {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        static bool Throws(Action fn){try{fn();return false;}catch(Exception){return true;}}
        static byte[] Hex(string text){return text.Split(' ').Select(x=>Convert.ToByte(x,16)).ToArray();}
        static MacroDefinition ExampleMacro(){var m=new MacroDefinition {Name="F13 测试",TargetIndex=1};m.Actions.Add(new MacroAction {Code=104,Down=true});m.Actions.Add(new MacroAction {Code=104,Down=false});return m;}
        [STAThread] public static int Main(string[] args) {
            try {
                Check(BatterySample.Parse(new byte[]{3,0,64,1,8}).Level==80,"real captured mouse packet = 80%");
                Check(BatterySample.Parse(new byte[]{3,1,64,1,10}).Level==100,"alternate event type");
                Check(!BatterySample.Parse(new byte[]{3,0,64,0,8}).Level.HasValue,"offline report clears stale battery");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1,0}).Level.HasValue,"unconfirmed zero is unknown");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1,11}).Level.HasValue,"reject out-of-range battery");
                Check(!BatterySample.Parse(new byte[]{3,0,63,1,8}).Recognized,"reject unrelated event");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1}).Recognized,"reject truncated packet");
                // Golden packet accepted by this receiver and physically confirmed cyan by the user.
                byte[] cyan={0x05,0x0D,0x01,0x01,0x04,0x08,0x46,0xD3,0xFF,0x06,0x01,0,0};
                Check(Protocol.LightingCommand(new Lighting()).SequenceEqual(cyan),"matches physically confirmed 13-byte cyan command");
                var draft=new Lighting {Mode=3,Brightness=4,R=19,G=32,B=99};
                Check(Protocol.LightingCommand(draft).SequenceEqual(new byte[]{5,13,1,3,4,4,19,32,99,6,1,0,0}),"mode brightness and RGB occupy verified command fields");
                draft.Enabled=false;Check(Protocol.LightingCommand(draft)[3]==0,"light disabled maps to Off");
                Check(Throws(()=>Protocol.LightingCommand(new Lighting {Brightness=9})),"reject invalid brightness");
                Check(Throws(()=>Protocol.LightingCommand(new Lighting {Mode=9})),"reject unnamed mode");
                var fake=new FakeSession();Protocol.Apply(fake,new Lighting(),CancellationToken.None);
                Check(fake.Writes==1&&fake.Sent.SequenceEqual(cyan),"apply sends short lighting command once without configuration read");
                using(var cancel=new CancellationTokenSource()){
                    fake=new FakeSession();cancel.Cancel();
                    Check(Throws(()=>Protocol.Apply(fake,new Lighting(),cancel.Token))&&fake.Writes==0,"cancelled operation never writes");
                }
                fake=new FakeSession {Accepted=false};Check(Throws(()=>Protocol.Apply(fake,new Lighting(),CancellationToken.None)),"device write rejection is reported");
                var dpi=new DpiScheme();
                var dpiGolden=Hex("04 32 01 08 08 3f 00 00 12 1b 24 37 49 73 00 00 12 1b 24 37 49 73 00 00 02 ff 00 00 00 ff 00 00 00 ff 00 ff ff ff ff 00 ff 00 ff ff ff ff ff ff ff 01");
                Check(Protocol.DpiCommand(dpi).SequenceEqual(dpiGolden),"1200 DPI default matches 50-byte vendor command layout");
                dpi.Values[0]=5200;var high=Protocol.DpiCommand(dpi);
                Check(high[6]==1&&high[7]==1&&high[8]==59&&high[16]==59,"high DPI uses both multiplier flags and correct sensor register");
                dpi.Values[0]=10000;high=Protocol.DpiCommand(dpi);Check(high[8]==115&&high[16]==115,"10000 DPI uses doubled 5000 register");
                dpi.Values[0]=5100;Check(Throws(()=>Protocol.DpiCommand(dpi)),"unrepresentable 5100 is rejected instead of silently rounding");
                dpi.Values[0]=100;Check(Throws(()=>Protocol.DpiCommand(dpi)),"reject DPI below supported range");
                Check(Throws(()=>Protocol.DpiCommand(new DpiScheme {Selected=6})),"reject disabled active DPI slot");
                Check(new[]{125,250,500,1000}.All(rate=>Protocol.PollingCommand(rate).SequenceEqual(new byte[]{6,9,1,(byte)(1000/rate),0,0,0,0,0})),"all four polling rates use standalone 9-byte commands");
                Check(Throws(()=>Protocol.PollingCommand(2000)),"reject unsupported polling rate");
                Check(Protocol.ParseDpiStage(new byte[]{3,0,16,2,0})==1,"DPI stage event is distinct from battery");
                Check(!Protocol.ParseDpiStage(new byte[]{3,0,64,1,8}).HasValue&&!Protocol.ParseDpiStage(new byte[]{3,0,16,9,0}).HasValue,"reject battery and out-of-range stage events");
                var sensitivity=new SensitivityModel();sensitivity.ObserveStage(1);
                Check(sensitivity.Hardware.Contains("数值未回读"),"initial hardware event never turns draft DPI into a measured value");
                sensitivity.Sent(new DpiScheme());sensitivity.ObserveStage(1);sensitivity.InvalidateDevice();sensitivity.ObserveStage(1);
                Check(sensitivity.Hardware.Contains("数值未回读"),"disconnect discards mapping from last sent scheme");
                fake=new FakeSession();Protocol.ApplyDpi(fake,new DpiScheme(),CancellationToken.None);
                Check(fake.History.Count==2&&fake.History[0].SequenceEqual(new byte[]{12,4,1,5})&&fake.History[1].SequenceEqual(dpiGolden),"DPI apply activates profile before writing settings");
                fake=new FakeSession {Accepted=false};
                Check(Throws(()=>Protocol.ApplyDpi(fake,new DpiScheme(),CancellationToken.None))&&fake.Writes==1,"failed profile activation stops DPI write");
                using(var cancel=new CancellationTokenSource()){
                    fake=new FakeSession {OnWrite=()=>cancel.Cancel()};
                    Check(Throws(()=>Protocol.ApplyDpi(fake,new DpiScheme(),cancel.Token))&&fake.Writes==1,"cancellation during activation prevents later DPI write");
                }
                fake=new FakeSession();Check(Throws(()=>Protocol.ApplyDpi(fake,new DpiScheme {Selected=9},CancellationToken.None))&&fake.Writes==0,"invalid DPI scheme never switches profile");
                fake=new FakeSession();Protocol.ApplyPolling(fake,500,CancellationToken.None);
                Check(fake.History.Count==2&&fake.History[0].SequenceEqual(new byte[]{12,4,1,5})&&fake.History[1].SequenceEqual(new byte[]{6,9,1,2,0,0,0,0,0}),"polling apply activates default profile independently of DPI");
                MacroChecks();
                if(args.Length>0&&args[0]=="--probe"){
                    var devices=Native.Find();foreach(var d in devices)Console.WriteLine(d.Product+" Page="+d.Caps.Page+" Input="+d.Caps.Input+" Feature="+d.Caps.Feature);
                    Check(devices.Exists(d=>d.Caps.Page==0xA&&d.Caps.Input==5),"real mouse battery interface detected");
                    Check(devices.Exists(d=>d.Caps.Page==0xB&&d.Caps.Feature==132),"real mouse lighting interface detected");
                    using(var signal=new ManualResetEvent(false))using(var monitor=new BatteryMonitor()){
                        int? live=null;monitor.Updated+=(sample,detail)=>{if(sample.Level.HasValue){live=sample.Level;signal.Set();}};
                        monitor.Start();
                        if(signal.WaitOne(8000)) {Check(live>=10&&live<=100,"native listener receives real live battery: "+live+"%");}
                        else Console.WriteLine("INFO: receiver found; no valid battery within 8 seconds; no cached value used");
                    }
                }
                if(args.Length>1&&args[0]=="--ui")Ui(args[1]);
                if(args.Length>1&&args[0]=="--live-lighting")LiveLighting(args[1]);
                if(args.Length>1&&args[0]=="--live-dpi")LiveDpi(args[1]);
                if(args.Length>1&&args[0]=="--live-macro")LiveMacro(args[1]);
                Console.WriteLine("TOTAL: "+count+" checks passed");return 0;
            }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(160)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void MacroChecks(){
            var macro=ExampleMacro();byte[] packet=MacroProtocol.MacroCommand(macro);
            Check(packet.Length==129&&packet.Take(8).SequenceEqual(new byte[]{9,129,4,0,0,0,0,1})&&packet[28]==2&&packet.Skip(29).Take(4).SequenceEqual(new byte[]{25,104,153,104}),"macro header and delay-before-key pairs match vendor serializer");
            macro.Actions[0].DelayText="500";packet=MacroProtocol.MacroCommand(macro);
            Check(packet[28]==3&&packet.Skip(29).Take(6).SequenceEqual(new byte[]{50,104,2,3,153,104}),"long delay splits into event remainder and 200 ms extension");
            macro=ExampleMacro();macro.Actions.RemoveAt(1);Check(Throws(()=>MacroProtocol.MacroCommand(macro)),"unreleased key is rejected before writing hardware");
            macro=ExampleMacro();macro.Actions[0].Down=false;Check(Throws(()=>MacroProtocol.MacroCommand(macro)),"unmatched key-up is rejected");
            macro=ExampleMacro();macro.Actions[0].DelayText="51";Check(Throws(()=>MacroProtocol.MacroCommand(macro)),"odd delay is rejected instead of silently losing precision");
            macro=ExampleMacro();macro.RepeatText="256";Check(Throws(()=>MacroProtocol.MacroCommand(macro)),"macro repetitions have finite device bounds");
            macro=ExampleMacro();var duplicate=ExampleMacro();Check(Throws(()=>MacroProtocol.ValidatePlan(new[]{macro,duplicate})),"duplicate physical triggers cannot silently overwrite each other");
            var blank=new MacroModel();var fake=new FakeSession();
            Check(blank.Selected.TargetIndex==0&&!blank.CanApply&&Throws(()=>MacroProtocol.Apply(fake,blank.Library,CancellationToken.None))&&fake.Writes==0,"new macro is unassigned and cannot implicitly change device buttons");
            var defaults=MacroProtocol.ButtonCommand(new MacroDefinition[0]);var mapping=MacroProtocol.ButtonCommand(new[]{macro});
            Check(defaults.Length==77&&defaults.Take(12).SequenceEqual(new byte[]{8,77,1,2,0,0,3,0,0,4,0,0})&&mapping[12]==18&&mapping[14]==4&&mapping.Take(12).SequenceEqual(defaults.Take(12)),"binding preserves standard left right and middle functions");
            fake=new FakeSession();MacroProtocol.Apply(fake,new[]{macro},CancellationToken.None);
            Check(fake.History.Count==4&&fake.History[1].SequenceEqual(defaults)&&fake.History[2].SequenceEqual(MacroProtocol.MacroCommand(macro))&&fake.History[3].SequenceEqual(mapping),"macro uploads while unassigned and binds only after complete upload");
            using(var cancel=new CancellationTokenSource()){
                fake=new FakeSession();var pending=fake;fake.OnWrite=()=>{if(pending.Writes==3)cancel.Cancel();};
                Check(Throws(()=>MacroProtocol.Apply(fake,new[]{macro},cancel.Token))&&fake.Writes==3,"cancellation after upload prevents final binding");
            }
            var many=ExampleMacro();many.Actions.Clear();for(int i=0;i<25;i++){many.Actions.Add(new MacroAction {Code=4,Down=true,DelayText="500"});many.Actions.Add(new MacroAction {Code=4,Down=false,DelayText="500"});}
            Check(Throws(()=>MacroProtocol.MacroCommand(many)),"expanded macro capacity is checked including long delays");
            string folder=Path.Combine(Path.GetTempPath(),"gmgear-macro-test-"+Guid.NewGuid().ToString("N"));string file=Path.Combine(folder,"macros.json");
            try{
                macro=ExampleMacro();macro.TargetIndex=0;MacroStorage.Save(file,new[]{macro});var loaded=MacroStorage.Load(file);
                Check(loaded.Count==1&&loaded[0].TargetIndex==0&&loaded[0].Actions[1].Code==104&&!loaded[0].Actions[1].Down,"macro persistence preserves explicit unassigned state and ordered events");
                MacroStorage.Save(file,new MacroDefinition[0]);Check(MacroStorage.Load(file).Count==0,"saving an empty library does not resurrect a deleted macro");
            }finally{if(File.Exists(file))File.Delete(file);if(Directory.Exists(folder))Directory.Delete(folder);}
        }
        static void MacroKeyEvent(Hub hub,System.Windows.Input.Key key,bool down){
            var e=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(hub.Window),Environment.TickCount,key);
            e.RoutedEvent=down?System.Windows.Input.Keyboard.PreviewKeyDownEvent:System.Windows.Input.Keyboard.PreviewKeyUpEvent;hub.Window.RaiseEvent(e);
        }
        // Explicitly authorized one-time rear-side-button test. Always restore default buttons.
        static void LiveMacro(string output){
            Directory.CreateDirectory(output);var app=new Application();
            using(var hub=new Hub(false)){
                hub.Window.Show();bool pressed=false,released=false;
                try{
                    var until=DateTime.UtcNow.AddSeconds(8);while(!hub.Model.HasDevice&&DateTime.UtcNow<until)Pump();
                    Check(hub.Model.HasDevice,"live macro UI detects receiver");hub.Route("Buttons");
                    hub.Model.Macros.Replace(new[]{ExampleMacro()});
                    using(var keyboard=new RawKeyboardProbe(hub.Window)){
                        keyboard.Received+=(key,down)=>{Console.WriteLine("GM RECEIVER KEY "+key+(down?" DOWN":" UP"));if(key==0x7C){if(down)pressed=true;else released=true;}};
                        hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));until=DateTime.UtcNow.AddSeconds(10);
                        while(hub.Model.Busy&&DateTime.UtcNow<until)Pump();
                        Check(hub.Model.Macros.Status=="方案已发送","macro UI sends finite F13 to explicitly authorized rear button");
                        hub.Model.Notice="实机测试：请按一次后侧键；测试后自动恢复默认按键。";
                        Console.WriteLine("MACRO ARMED "+DateTime.UtcNow.ToString("O")+": press rear side button; listening up to 180 seconds");
                        until=DateTime.UtcNow.AddSeconds(180);while(!(pressed&&released)&&DateTime.UtcNow<until)Pump();
                        Capture(hub,Path.Combine(output,"gear-live-macro.png"));
                    }
                }finally{
                    using(var session=new FeatureSession())MacroProtocol.Restore(session,CancellationToken.None);
                    Console.WriteLine("RESTORED DEFAULT BUTTONS; no default macro binding retained");hub.Exit();app.Shutdown();
                }
                Check(pressed&&released,"physical receiver emits F13 down and up when rear button is pressed");
            }
        }
        static void Capture(Hub hub,string file){Pump();var root=(FrameworkElement)hub.Window.Content;root.UpdateLayout();var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(file))png.Save(output);}
        static void Ui(string output){
            Directory.CreateDirectory(output);var app=new Application();
            using(var hub=new Hub(true)){
                hub.Window.Show();Pump();Capture(hub,Path.Combine(output,"gear-home-preview.png"));
                hub.B("ConfigureButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(((FrameworkElement)hub.Window.FindName("LightingPage")).Visibility==Visibility.Visible,"home opens lighting page");
                hub.B("ColorPink").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(hub.Model.ColorHex=="#FF77B4","color preset updates model");
                ((Slider)hub.Window.FindName("BrightnessSlider")).Value=3;Check(hub.Model.Brightness==3,"brightness slider updates model");
                hub.Model.ColorHex="#invalid";Check(!hub.Model.CanApply,"invalid HEX disables apply");
                hub.Model.ColorHex="#46D3FF";hub.Model.Brightness=8;
                Capture(hub,Path.Combine(output,"gear-lighting-preview.png"));
                hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(hub.Model.Notice.Contains("不会写入"),"preview does not access device writes");
                hub.B("FeatureDpi").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(((FrameworkElement)hub.Window.FindName("DpiPage")).Visibility==Visibility.Visible&&hub.Model.ApplyLabel=="应用 DPI 方案","DPI card opens functional sensitivity page");
                var stages=(ItemsControl)hub.Window.FindName("DpiStages");
                var item=(ContentPresenter)stages.ItemContainerGenerator.ContainerFromIndex(0);
                ((RadioButton)VisualTreeHelper.GetChild(item,0)).IsChecked=true;Pump();
                Check(hub.Model.Sensitivity.SelectedIndex==0&&hub.Model.Sensitivity.EditText=="800","DPI tile selects stage through two-way binding");
                var dpiBox=(TextBox)hub.Window.FindName("DpiValueBox");dpiBox.Text="5100";Pump();
                Check(!hub.Model.CanApply&&hub.Model.CanApplyPolling&&hub.Model.Sensitivity.Error.Length>0,"invalid DPI blocks scheme apply but not independent polling");
                ((Slider)hub.Window.FindName("DpiSlider")).Value=6700;Pump();
                Check(hub.Model.Sensitivity.EditText=="6800"&&hub.Model.CanApply,"slider quantizes high DPI to supported increments");
                hub.Model.Sensitivity.Load(new DpiScheme());Pump();Capture(hub,Path.Combine(output,"gear-dpi-preview.png"));
                hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(hub.Model.Notice.Contains("不会写入"),"preview DPI apply never writes hardware");
                hub.B("FeatureButtons").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(((FrameworkElement)hub.Window.FindName("ButtonsPage")).Visibility==Visibility.Visible&&hub.Model.Macros.Selected.TargetIndex==0&&!hub.Model.CanApply,"macro page opens with no default binding");
                hub.B("MacroInsertButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(hub.Model.Macros.Selected.Actions.Count==2,"manual click insertion creates balanced down-up actions");
                hub.B("MacroClearButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));hub.B("MacroRecordButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                MacroKeyEvent(hub,System.Windows.Input.Key.LeftCtrl,true);MacroKeyEvent(hub,System.Windows.Input.Key.C,true);MacroKeyEvent(hub,System.Windows.Input.Key.C,false);MacroKeyEvent(hub,System.Windows.Input.Key.LeftCtrl,false);MacroKeyEvent(hub,System.Windows.Input.Key.Escape,true);
                Check(!hub.Model.Macros.Recording&&hub.Model.Macros.Selected.Actions.Count==4&&hub.Model.Macros.Selected.Actions[0].Code==224,"window-scoped recorder records modifiers and stops on Escape");
                ((ComboBox)hub.Window.FindName("MacroTargetBox")).SelectedIndex=1;Pump();Check(hub.Model.CanApply,"explicit trigger selection enables a valid macro scheme");
                hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(hub.Model.Notice.Contains("不会写入"),"preview macro apply never writes to device");
                hub.Model.Macros.Selected.TargetIndex=0;hub.Model.Macros.Selected.Name="示例：Ctrl + C";hub.Model.Notice="界面预览 · 示例宏动作 · 不会读取或更改设备";
                Capture(hub,Path.Combine(output,"gear-macros-preview.png"));
                hub.Route("Settings");Capture(hub,Path.Combine(output,"gear-settings-preview.png"));
                hub.Window.Width=1040;hub.Window.Height=720;hub.Route("Lighting");Pump();
                var button=hub.B("ApplyButton");Check(button.ActualWidth>=150&&button.ActualHeight>30,"apply remains usable at minimum window size");
                hub.Route("Dpi");Capture(hub,Path.Combine(output,"gear-dpi-small-preview.png"));
                hub.Model.HasDevice=false;Check(!hub.Model.CanApply,"disconnected device disables apply");
                hub.Exit();
            }
            app.Shutdown();
        }
        // Opt-in live DPI integration test. Installs six DPI stages with 1200 selected.
        // Polling is sent only when this test is explicitly extended to do so.
        static void LiveDpi(string output){
            Directory.CreateDirectory(output);var app=new Application();
            using(var hub=new Hub(false)){
                hub.Window.Show();var until=DateTime.UtcNow.AddSeconds(8);
                while(!hub.Model.HasDevice&&DateTime.UtcNow<until)Pump();
                Check(hub.Model.HasDevice,"live sensitivity UI detects receiver");
                hub.Route("Dpi");hub.Model.Sensitivity.Load(new DpiScheme());
                hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(6);
                while(hub.Model.Busy&&DateTime.UtcNow<until)Pump();
                Check(!hub.Model.Busy&&hub.Model.Sensitivity.Status=="已发送 1200 DPI","live Apply button sends 1200 DPI scheme to receiver");
                hub.Model.Sensitivity.PollingIndex=2;hub.B("ApplyPollingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(6);while(hub.Model.Busy&&DateTime.UtcNow<until)Pump();
                Check(!hub.Model.Busy&&hub.Model.Sensitivity.RateStatus=="已发送 500 Hz","live polling button sends 500 Hz independently");
                Console.WriteLine("LIVE DPI: "+hub.Model.Notice+" / "+hub.Model.Sensitivity.Hardware);
                Capture(hub,Path.Combine(output,"gear-live-dpi.png"));hub.Exit();
            }
            app.Shutdown();
        }
        // Explicit opt-in integration test. Sends ONLY the confirmed cyan lighting command.
        static void LiveLighting(string output){
            Directory.CreateDirectory(output);var app=new Application();
            using(var hub=new Hub(false)){
                hub.Window.Show();var until=DateTime.UtcNow.AddSeconds(8);
                while(!hub.Model.HasDevice&&DateTime.UtcNow<until)Pump();
                Check(hub.Model.HasDevice,"live UI detects receiver");
                hub.Route("Lighting");hub.Model.ModeIndex=0;hub.Model.Brightness=8;
                hub.Model.LightEnabled=true;hub.Model.ColorHex="#46D3FF";
                hub.B("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(6);
                while(hub.Model.Busy&&DateTime.UtcNow<until)Pump();
                Check(!hub.Model.Busy&&hub.Model.DraftStatus=="已发送","live Apply button sends cyan to physical mouse");
                Console.WriteLine("LIVE UI: "+hub.Model.Notice);
                Capture(hub,Path.Combine(output,"gear-live-lighting.png"));hub.Exit();
            }
            app.Shutdown();
        }
    }
}
