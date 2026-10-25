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
        public byte[] Current; public byte[] Sent; public int Writes,Reads;
        public bool Accepted=true; public Action OnRead;
        public byte[] Read(){Reads++;if(OnRead!=null)OnRead();return Current;}
        public bool Write(byte[] b){Writes++;Sent=b;return Accepted;}
        public void Cancel(){}public void Dispose(){}
    }
    internal static class Tests {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        static bool Throws(Action fn){try{fn();return false;}catch(Exception){return true;}}
        static byte[] Configuration(){var bytes=new byte[132];new Random(42).NextBytes(bytes);bytes[0]=5;bytes[1]=13;return bytes;}
        [STAThread] public static int Main(string[] args) {
            try {
                Check(BatterySample.Parse(new byte[]{3,0,64,1,8}).Level==80,"real captured mouse packet = 80%");
                Check(BatterySample.Parse(new byte[]{3,1,64,1,10}).Level==100,"alternate event type");
                Check(!BatterySample.Parse(new byte[]{3,0,64,0,8}).Level.HasValue,"offline report clears stale battery");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1,0}).Level.HasValue,"unconfirmed zero is unknown");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1,11}).Level.HasValue,"reject out-of-range battery");
                Check(!BatterySample.Parse(new byte[]{3,0,63,1,8}).Recognized,"reject unrelated event");
                Check(!BatterySample.Parse(new byte[]{3,0,64,1}).Recognized,"reject truncated packet");
                var cfg=Configuration();var before=(byte[])cfg.Clone();var draft=new Lighting {Mode=3,Brightness=4,R=19,G=32,B=99};
                var patched=Protocol.Patch(cfg,draft);
                Check(patched[3]==3&&patched[5]==4&&patched[6]==19&&patched[7]==32&&patched[8]==99,"patch exactly the requested lighting fields");
                Check(Enumerable.Range(0,132).All(i=>new[]{3,5,6,7,8}.Contains(i)||patched[i]==before[i]),"all 127 unknown bytes stay unchanged");
                Check(cfg.SequenceEqual(before),"original configuration remains intact");
                draft.Enabled=false;Check(Protocol.Patch(cfg,draft)[3]==0,"light disabled maps to Off");
                draft.ReportIdPrefix=true;var prefixed=Protocol.Patch(cfg,draft);
                Check(prefixed[0]==4&&prefixed.Skip(1).SequenceEqual(Protocol.Patch(cfg,new Lighting {Mode=3,Brightness=4,R=19,G=32,B=99,Enabled=false}).Skip(1)),"prefix compatibility changes only report byte");
                Check(Throws(()=>Protocol.Patch(null,draft)),"missing configuration never uses a foreign template");
                Check(Throws(()=>Protocol.Patch(new byte[132],draft)),"reject invalid configuration signature");
                Check(Throws(()=>Protocol.Patch(new byte[131],draft)),"reject wrong feature length");
                Check(Throws(()=>Protocol.Patch(cfg,new Lighting {Brightness=9})),"reject invalid brightness");
                Check(Throws(()=>Protocol.Patch(cfg,new Lighting {Mode=9})),"reject unnamed mode");
                var fake=new FakeSession {Current=cfg};Protocol.Apply(fake,new Lighting(),CancellationToken.None);
                Check(fake.Writes==1,"successful apply writes once");
                fake=new FakeSession {Current=new byte[132]};
                Check(Throws(()=>Protocol.Apply(fake,new Lighting(),CancellationToken.None))&&fake.Writes==0,"failed reads never write");
                using(var cancel=new CancellationTokenSource()){
                    fake=new FakeSession {Current=cfg,OnRead=()=>cancel.Cancel()};
                    Check(Throws(()=>Protocol.Apply(fake,new Lighting(),cancel.Token))&&fake.Writes==0,"cancellation after read prevents late write");
                }
                fake=new FakeSession {Current=cfg,Accepted=false};Check(Throws(()=>Protocol.Apply(fake,new Lighting(),CancellationToken.None)),"device write rejection is reported");
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
    }
}
