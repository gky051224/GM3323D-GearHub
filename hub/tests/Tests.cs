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
        public bool Accepted=true;
        public bool Write(byte[] b){Writes++;Sent=b;return Accepted;}
        public void Cancel(){}public void Dispose(){}
    }
    internal static class Tests {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        static bool Throws(Action fn){try{fn();return false;}catch(Exception){return true;}}
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
                hub.Route("Dpi");Check(((TextBlock)hub.Window.FindName("PendingDescription")).Text.Contains("不提供写入"),"DPI page clearly marks unsupported controls");
                hub.Route("Buttons");Check(((TextBlock)hub.Window.FindName("PendingDescription")).Text.Contains("不提供写入"),"macro page clearly marks unsupported controls");
                hub.Route("Settings");Capture(hub,Path.Combine(output,"gear-settings-preview.png"));
                hub.Window.Width=1040;hub.Window.Height=720;hub.Route("Lighting");Pump();
                var button=hub.B("ApplyButton");Check(button.ActualWidth>=150&&button.ActualHeight>30,"apply remains usable at minimum window size");
                hub.Model.HasDevice=false;Check(!hub.Model.CanApply,"disconnected device disables apply");
                hub.Exit();
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
