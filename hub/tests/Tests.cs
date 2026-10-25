using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GearHub {
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
                Console.WriteLine("TOTAL: "+count+" checks passed");return 0;
            }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(160)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
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
                hub.Route("Buttons");Check(((TextBlock)hub.Window.FindName("PendingDescription")).Text.Contains("不提供写入"),"macro page clearly marks unsupported controls");
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
