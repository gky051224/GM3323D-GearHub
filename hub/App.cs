using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace GearHub {
    public sealed class ViewModel : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        public SensitivityModel Sensitivity {get;private set;}
        public MacroModel Macros {get;private set;}
        public ViewModel(){Sensitivity=new SensitivityModel();Sensitivity.PropertyChanged+=(s,e)=>{Notify("CanApply");};Macros=new MacroModel();Macros.PropertyChanged+=(s,e)=>{Notify("CanApply");Notify("CanRestoreButtons");};}
        public Action DraftChanged;
        void Notify(string name) {var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs(name));}
        public void All() {Notify("");}
        string header="你的设备",subhead="一个工作空间，掌控你的鼠标。",notice="正在检测 GM3323D 接收器…",connection="正在检测…",battery="—",draftStatus="预览未应用";
        int? level;bool hasDevice,busy,enabled=true,dpiPage,macroPage;int modeIndex;double brightness=8,hue=194;string color="#46D3FF";
        public string Header {get{return header;}set{header=value;Notify("Header");}}
        public string Subhead {get{return subhead;}set{subhead=value;Notify("Subhead");}}
        public string Notice {get{return notice;}set{notice=value;Notify("Notice");}}
        public string Connection {get{return connection;}set{connection=value;Notify("Connection");Notify("ConnectionBrush");}}
        public System.Windows.Media.Brush ConnectionBrush {get{return new SolidColorBrush(hasDevice?System.Windows.Media.Color.FromRgb(140,231,206):System.Windows.Media.Color.FromRgb(194,201,213));}}
        public string Battery {get{return battery;}}
        public double BatteryValue {get{return level.HasValue?level.Value:0;}}
        public bool HasDevice {get{return hasDevice;}set{hasDevice=value;Notify("HasDevice");Notify("CanApply");Notify("CanApplyPolling");Notify("CanRestoreButtons");Notify("ConnectionBrush");}}
        public bool Busy {get{return busy;}set{busy=value;Notify("CanApply");Notify("CanApplyPolling");Notify("CanRestoreButtons");Notify("CanEdit");Notify("ApplyLabel");}}
        public bool CanEdit {get{return !busy;}}
        public bool DpiPage {get{return dpiPage;}set{dpiPage=value;Notify("CanApply");Notify("ApplyLabel");}}
        public bool MacroPage {get{return macroPage;}set{macroPage=value;Notify("CanApply");Notify("ApplyLabel");}}
        public bool CanApply {get{return hasDevice&&!busy&&(macroPage?Macros.CanApply:(dpiPage?Sensitivity.Valid:ValidColor));}}
        public bool CanRestoreButtons {get{return hasDevice&&!busy&&!Macros.Recording;}}
        public bool CanApplyPolling {get{return hasDevice&&!busy;}}
        public string ApplyLabel {get{return busy?"正在应用…":(macroPage?"应用按键方案":(dpiPage?"应用 DPI 方案":"应用到鼠标"));}}
        public string DraftStatus {get{return draftStatus;}set{draftStatus=value;Notify("DraftStatus");}}
        public string[] Modes {get{return new[]{"固定色","呼吸","霓虹","循环呼吸","固定色 · 随 DPI","呼吸 · 随 DPI","流光","跑马灯"};}}
        void Dirty() {DraftStatus="预览未应用";if(DraftChanged!=null)DraftChanged();}
        public int ModeIndex {get{return modeIndex;}set{if(value<0||value>7)return;modeIndex=value;Notify("ModeIndex");Dirty();}}
        public double Brightness {get{return brightness;}set{brightness=Math.Max(1,Math.Min(8,Math.Round(value)));Notify("Brightness");Notify("BrightnessLabel");Dirty();}}
        public string BrightnessLabel {get{return ((int)brightness)+" / 8";}}
        public bool LightEnabled {get{return enabled;}set{enabled=value;Notify("LightEnabled");Notify("PreviewBrush");Dirty();}}
        public string ColorHex {get{return color;}set{
            color=value??"";
            if(ValidColor){var c=System.Drawing.ColorTranslator.FromHtml(color);if(c.R!=c.G||c.G!=c.B){hue=c.GetHue();Notify("Hue");Notify("HueBrush");}}
            Notify("ColorHex");Notify("PreviewBrush");Notify("CanApply");Dirty();
        }}
        public double Hue {get{return hue;}set{value=Math.Max(0,Math.Min(359,value));if(Math.Abs(value-hue)<0.1)return;hue=value;Notify("Hue");Notify("HueBrush");ColorHex=Hsv(value,1,1);}}
        public System.Windows.Media.Brush HueBrush {get{return (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(Hsv(hue,1,1));}}
        public static string Hsv(double h,double s,double v){
            double c=v*s,x=c*(1-Math.Abs((h/60)%2-1)),m=v-c,r=0,g=0,b=0;
            if(h<60){r=c;g=x;}else if(h<120){r=x;g=c;}else if(h<180){g=c;b=x;}else if(h<240){g=x;b=c;}else if(h<300){r=x;b=c;}else{r=c;b=x;}
            return String.Format("#{0:X2}{1:X2}{2:X2}",(int)Math.Round((r+m)*255),(int)Math.Round((g+m)*255),(int)Math.Round((b+m)*255));
        }
        public bool ValidColor {get{uint rgb;return color!=null&&color.Length==7&&color[0]=='#'&&UInt32.TryParse(color.Substring(1),System.Globalization.NumberStyles.HexNumber,null,out rgb);}}
        public System.Windows.Media.Brush PreviewBrush {get{
            if(!enabled)return new SolidColorBrush(System.Windows.Media.Color.FromRgb(65,75,88));
            if(!ValidColor)return new SolidColorBrush(System.Windows.Media.Color.FromRgb(70,211,255));
            return (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color);
        }}
        public ImageSource MouseImage {get{return MouseArtwork.Image;}}
        public System.Windows.Media.Color PreviewColor {get{return ValidColor?(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColorHex):System.Windows.Media.Colors.Cyan;}}
        public double PreviewPower {get{return LightEnabled?(0.25+0.75*Brightness/8):0;}}
        public void SetBattery(int? value) {level=value;battery=value.HasValue?value.Value+"%":"—";Notify("Battery");Notify("BatteryValue");}
        public Lighting Draft() {
            if(!ValidColor)throw new ArgumentException("请输入 #RRGGBB 格式的颜色。");
            var c=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
            return new Lighting {Mode=modeIndex+1,Brightness=(int)brightness,Enabled=enabled,R=c.R,G=c.G,B=c.B};
        }
    }

    internal sealed class Hub : IDisposable {
        internal readonly Window Window;
        internal readonly ViewModel Model=new ViewModel();
        internal readonly bool Preview;
        readonly BatteryMonitor monitor=new BatteryMonitor();
        Forms.NotifyIcon tray;
        System.Drawing.Icon trayIcon;
        bool exiting,disposed;
        readonly HashSet<byte> recordedKeys=new HashSet<byte>();
        readonly Stopwatch recordClock=new Stopwatch();MacroDefinition recordTarget;
        DateTime operationUntil=DateTime.MinValue;
        readonly Dictionary<string,FrameworkElement> pages=new Dictionary<string,FrameworkElement>();
        readonly Dictionary<string,Button> nav=new Dictionary<string,Button>();
        const string RegistryPath=@"Software\GM3323DGearHub";
        const string RunName="GM3323DGearHub";
        public Hub(bool preview) {
            Preview=preview;
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("GearHub.MainWindow.xaml")) Window=(Window)XamlReader.Load(stream);
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("GearHub.AppIcon")) Window.Icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            Window.DataContext=Model;
            Window.Width=Math.Min(1180,SystemParameters.WorkArea.Width-48);
            Window.Height=Math.Min(800,SystemParameters.WorkArea.Height-48);
            Window.MinWidth=Math.Min(1040,Window.Width);Window.MinHeight=Math.Min(720,Window.Height);
            foreach(string name in new[]{"Home","Lighting","Dpi","Buttons","Settings","About"}) pages[name]=(FrameworkElement)Window.FindName(name+"Page");
            foreach(string name in new[]{"Home","Lighting","Dpi","Buttons","Settings","About"}) {
                string route=name;Button button=B("Nav"+name);nav[name]=button;button.Click+=(s,e)=>Route(route);
            }
            B("ConfigureButton").Click+=(s,e)=>Route("Lighting");B("FeatureLighting").Click+=(s,e)=>Route("Lighting");
            B("WindowMin").Click+=(s,e)=>Window.WindowState=WindowState.Minimized;
            B("WindowMax").Click+=(s,e)=>Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
            B("WindowClose").Click+=(s,e)=>Window.Close();
            ((FrameworkElement)Window.FindName("TitleDrag")).MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2)B("WindowMax").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else Window.DragMove();};
            B("MenuButton").Click+=(s,e)=>Route("Settings");
            B("FeatureDpi").Click+=(s,e)=>Route("Dpi");B("FeatureButtons").Click+=(s,e)=>Route("Buttons");
            B("HideButton").Click+=(s,e)=>Window.Hide();
            B("ExitButton").Click+=(s,e)=>Exit();
            B("RefreshButton").Click+=(s,e)=>{operationUntil=DateTime.UtcNow.AddSeconds(10);Model.Notice="正在重新检测，移动鼠标后会更新电量…";Model.SetBattery(null);};
            B("ApplyButton").Click+=async(s,e)=>{if(Model.MacroPage)await ApplyMacros();else if(Model.DpiPage)await ApplyDpi();else await Apply();};
            B("ApplyPollingButton").Click+=async(s,e)=>await ApplyPolling();
            B("SaveDraftButton").Click+=(s,e)=>{if(Model.MacroPage)SaveMacros();else if(Model.DpiPage)SaveSensitivity();else SaveDraft();};
            B("DpiDefaultButton").Click+=(s,e)=>Model.Sensitivity.Load(new DpiScheme());
            B("MacroNewButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.Add());
            B("MacroPresetButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.AddPreset());
            B("MacroDeleteButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.Remove());
            B("MacroInsertButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.Insert());
            B("MacroUpButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.Move(-1));
            B("MacroDownButton").Click+=(s,e)=>MacroEdit(()=>Model.Macros.Move(1));
            B("MacroRemoveAction").Click+=(s,e)=>MacroEdit(()=>Model.Macros.DeleteAction());
            B("MacroClearButton").Click+=(s,e)=>MacroEdit(()=>{if(Model.Macros.Selected!=null)Model.Macros.Selected.Actions.Clear();});
            B("MacroRecordButton").Click+=(s,e)=>{if(Model.Macros.Recording)StopRecording();else StartRecording();};
            B("MacroRestoreButton").Click+=async(s,e)=>await RestoreButtons();
            Window.PreviewKeyDown+=(s,e)=>RecordKey(e,true);Window.PreviewKeyUp+=(s,e)=>RecordKey(e,false);
            Window.Deactivated+=(s,e)=>StopRecording();
            Model.Macros.PropertyChanged+=(s,e)=>{if(e.PropertyName=="Selected"&&Model.Macros.Recording)StopRecording();};
            B("PickColor").Click+=(s,e)=>PickColor();
            B("SourceButton").Click+=(s,e)=>Process.Start(new ProcessStartInfo("https://github.com/Evan816Z/GM3323D-MouseLight") {UseShellExecute=true});
            foreach(string name in new[]{"Cyan","Purple","Pink","Orange","Green","White"}) B("Color"+name).Click+=(s,e)=>Model.ColorHex=(string)((Button)s).Tag;
            var square=(FrameworkElement)Window.FindName("ColorSquare");
            square.MouseLeftButtonDown+=(s,e)=>{square.CaptureMouse();SelectColor(e.GetPosition(square));};
            square.MouseMove+=(s,e)=>{if(square.IsMouseCaptured)SelectColor(e.GetPosition(square));};
            square.MouseLeftButtonUp+=(s,e)=>square.ReleaseMouseCapture();
            Model.PropertyChanged+=(s,e)=>{if(e.PropertyName=="ColorHex")UpdateMarker();};
            Model.PropertyChanged+=(s,e)=>{
                if(e.PropertyName=="ColorHex"||e.PropertyName=="LightEnabled"||e.PropertyName=="Brightness")UpdateArtwork();
            };
            square.SizeChanged+=(s,e)=>UpdateMarker();
            var boot=(CheckBox)Window.FindName("AutostartCheck");
            boot.IsChecked=!preview&&ReadAutostart();
            boot.Click+=(s,e)=>ChangeAutostart(boot.IsChecked==true);
            Window.Closing+=(s,e)=>{if(!exiting&&!Preview&&((CheckBox)Window.FindName("CloseToTray")).IsChecked==true){e.Cancel=true;Window.Hide();}};
            Window.Closed+=(s,e)=>Dispose();
            Window.SourceInitialized+=(s,e)=>SetupNativeWindow();
            Window.Loaded+=(s,e)=>UpdateArtwork();
            Window.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Route("Home");if(e.Key==System.Windows.Input.Key.F5)B("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            Route("Home");
            if(!preview)LoadDraft();
            if(preview){Model.HasDevice=true;Model.Connection="无线连接正常";Model.SetBattery(80);Model.Notice="界面预览 · 示例电量 80% · 不会读取或更改设备";}
            else {
                CreateTray();
                monitor.Updated+=(sample,detail)=>{
                    if(disposed)return;
                    Window.Dispatcher.BeginInvoke(new Action(()=>{
                        if(disposed)return;
                        Model.HasDevice=sample.Online;Model.Connection=detail;Model.SetBattery(sample.Level);
                        if(!sample.Online)Model.Sensitivity.InvalidateDevice();
                        if(!Model.Busy&&DateTime.UtcNow>operationUntil)Model.Notice=detail+"  ·  "+DateTime.Now.ToString("HH:mm:ss")+" 更新";
                        UpdateTray(sample.Level);
                    }));
                };
                monitor.DpiStageChanged+=stage=>{
                    if(!disposed)Window.Dispatcher.BeginInvoke(new Action(()=>{if(!disposed)Model.Sensitivity.ObserveStage(stage);}));
                };
                monitor.Start();
            }
        }
        internal Button B(string name) {return (Button)Window.FindName(name);}
        internal void Route(string route) {
            if(route!="Buttons")StopRecording();
            foreach(var page in pages.Values)page.Visibility=Visibility.Collapsed;
            foreach(var item in nav)item.Value.Tag=item.Key==route?"Active":null;
            string pageName=route;Model.DpiPage=route=="Dpi";Model.MacroPage=route=="Buttons";
            pages[pageName].Visibility=Visibility.Visible;
            B("ApplyButton").Visibility=route=="Lighting"||route=="Dpi"||route=="Buttons"?Visibility.Visible:Visibility.Collapsed;
            B("SaveDraftButton").Visibility=route=="Lighting"||route=="Dpi"||route=="Buttons"?Visibility.Visible:Visibility.Collapsed;
            ((FrameworkElement)Window.FindName("Sidebar")).Visibility=route=="Home"?Visibility.Collapsed:Visibility.Visible;
            if(route=="Home"){Model.Header="桌面";Model.Subhead="默认配置";}
            else if(route=="Lighting"){Model.Header="灯光效果";Model.Subhead="GM3323D  /  LIGHTING";}
            else if(route=="Dpi"){Model.Header="灵敏度 · DPI";Model.Subhead="GM3323D  /  SENSITIVITY";}
            else if(route=="Buttons") {
                Model.Header="按键与宏";Model.Subhead="GM3323D  /  ASSIGNMENTS";
            } else if(route=="Settings"){Model.Header="偏好设置";Model.Subhead="仅作用于 GM GEAR";}
            else {Model.Header="关于";Model.Subhead="独立项目  /  开源协议";}
            Window.Dispatcher.BeginInvoke(new Action(UpdateArtwork),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        void SelectColor(System.Windows.Point p){
            var square=(FrameworkElement)Window.FindName("ColorSquare");
            Model.ColorHex=ViewModel.Hsv(Model.Hue,Math.Max(0,Math.Min(1,p.X/Math.Max(1,square.ActualWidth))),1-Math.Max(0,Math.Min(1,p.Y/Math.Max(1,square.ActualHeight))));
        }
        void UpdateMarker(){
            if(!Model.ValidColor)return;
            var square=(FrameworkElement)Window.FindName("ColorSquare");var marker=(FrameworkElement)Window.FindName("PickerMarker");
            var c=System.Drawing.ColorTranslator.FromHtml(Model.ColorHex);double max=Math.Max(c.R,Math.Max(c.G,c.B)),min=Math.Min(c.R,Math.Min(c.G,c.B));
            Canvas.SetLeft(marker,(max==0?0:(max-min)/max)*square.ActualWidth-5);Canvas.SetTop(marker,(1-max/255)*square.ActualHeight-5);
        }
        void UpdateArtwork(){
            if(disposed)return;
            foreach(var image in ArtworkImages(Window)){
                if(!Object.Equals(image.Tag,"ProductImage"))continue;
                try {
                    var effect=image.Effect as RgbLightEffect;
                    if(effect==null){effect=new RgbLightEffect();image.Effect=effect;}
                    effect.Tint=Model.PreviewColor;effect.Power=Model.PreviewPower;
                }catch(Exception){image.Effect=null;}
            }
        }
        static IEnumerable<System.Windows.Controls.Image> ArtworkImages(DependencyObject parent){
            int count=VisualTreeHelper.GetChildrenCount(parent);
            for(int i=0;i<count;i++){
                var child=VisualTreeHelper.GetChild(parent,i);var image=child as System.Windows.Controls.Image;
                if(image!=null)yield return image;
                foreach(var next in ArtworkImages(child))yield return next;
            }
        }
        async Task Apply() {
            if(Preview){Model.Notice="界面预览不会写入设备。";return;}
            if(!Model.CanApply)return;
            Lighting draft=Model.Draft();Model.Busy=true;Model.Notice="正在应用鼠标灯光…";
            try {
                using(var cancel=new CancellationTokenSource(3500)) {
                    await Task.Run(()=>{
                        using(var session=new FeatureSession())
                        using(cancel.Token.Register(session.Cancel)) Protocol.Apply(session,draft,cancel.Token);
                    });
                }
                Model.DraftStatus="已发送";Model.Notice="设置已被接口接受，请查看鼠标灯光。";
            } catch(OperationCanceledException){Model.Notice="设备响应超时，未确认应用成功，请移动鼠标后重试。";}
              catch(Exception ex){Model.Notice=ex.Message;}
            finally {operationUntil=DateTime.UtcNow.AddSeconds(20);Model.Busy=false;}
        }
        async Task<bool> SendSetting(Action<IFeatureSession,CancellationToken> send,int timeout=3500){
            Model.Busy=true;
            try{
                using(var cancel=new CancellationTokenSource(timeout)){
                    await Task.Run(()=>{using(var session=new FeatureSession())using(cancel.Token.Register(session.Cancel))send(session,cancel.Token);});
                }
                return true;
            }catch(OperationCanceledException){Model.Notice="设备响应超时，请唤醒鼠标后重试。";}
            catch(Exception ex){Model.Notice=ex.Message;}
            finally{operationUntil=DateTime.UtcNow.AddSeconds(25);Model.Busy=false;}
            return false;
        }
        async Task ApplyDpi(){
            if(Preview){Model.Notice="界面预览不会写入设备。";return;}
            if(!Model.CanApply)return;
            var draft=Model.Sensitivity.Draft();Model.Notice="正在应用 DPI 方案…";
            if(await SendSetting((session,cancel)=>Protocol.ApplyDpi(session,draft,cancel))){
                Model.Sensitivity.Sent(draft);Model.Notice="已发送 "+draft.Values[draft.Selected]+" DPI，使用鼠标默认配置（第 1 组）。";
            }
        }
        async Task ApplyPolling(){
            if(Preview){Model.Notice="界面预览不会写入设备。";return;}
            if(!Model.CanApplyPolling)return;
            int rate=Model.Sensitivity.PollingRate;Model.Notice="正在应用回报率…";
            if(await SendSetting((session,cancel)=>Protocol.ApplyPolling(session,rate,cancel))){Model.Sensitivity.RateStatus="已发送 "+rate+" Hz";Model.Notice="已发送 "+rate+" Hz 回报率。";}
        }
        void MacroEdit(Action edit){StopRecording();try{edit();Model.Notice="本机宏草稿已修改；应用后才更改鼠标。";}catch(Exception ex){Model.Notice=ex.Message;}operationUntil=DateTime.UtcNow.AddSeconds(20);}
        void StartRecording(){
            if(Model.Macros.Selected==null||Model.Busy)return;
            recordTarget=Model.Macros.Selected;recordedKeys.Clear();recordClock.Restart();Model.Macros.Recording=true;
            Model.Notice="正在本窗口录制键盘；Esc 或失去焦点时停止。";operationUntil=DateTime.UtcNow.AddMinutes(5);
            System.Windows.Input.Keyboard.ClearFocus();Window.Focus();
        }
        void StopRecording(){
            if(!Model.Macros.Recording)return;
            foreach(byte code in recordedKeys)if(recordTarget.Actions.Count<50)recordTarget.Actions.Add(new MacroAction {Code=code,Down=false,DelayText="50"});
            recordedKeys.Clear();recordTarget=null;recordClock.Stop();Model.Macros.Recording=false;
            Model.Notice="录制已停止；请检查动作，再选择触发键。";operationUntil=DateTime.UtcNow.AddSeconds(20);
        }
        void RecordKey(System.Windows.Input.KeyEventArgs e,bool down){
            if(!Model.Macros.Recording)return;e.Handled=true;
            var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
            if(key==System.Windows.Input.Key.Escape){if(down)StopRecording();return;}
            byte? code=MacroKeys.FromWpf(key);if(!code.HasValue)return;
            if(down&&recordedKeys.Contains(code.Value))return;
            if(!down&&!recordedKeys.Contains(code.Value))return;
            if(down&&recordTarget.Actions.Count+recordedKeys.Count+2>50){StopRecording();return;}
            if(recordTarget.Actions.Count>0)recordTarget.Actions[recordTarget.Actions.Count-1].DelayText=(Math.Min(10000,(int)Math.Round(recordClock.Elapsed.TotalMilliseconds/2)*2)).ToString();
            if(down)recordedKeys.Add(code.Value);else recordedKeys.Remove(code.Value);
            recordTarget.Actions.Add(new MacroAction {Code=code.Value,Down=down,DelayText="50"});recordClock.Restart();
        }
        async Task ApplyMacros(){
            StopRecording();if(Preview){Model.Notice="界面预览不会写入设备。";return;}
            if(!Model.CanApply)return;
            var plan=Model.Macros.Snapshot();Model.Notice="正在写入宏与按键方案…";
            if(await SendSetting((session,cancel)=>MacroProtocol.Apply(session,plan,cancel),8000)){
                Model.Macros.Status="方案已发送";Model.Notice="宏与按键方案已发送；实际触发效果请在目标程序中验证。";
            }
        }
        async Task RestoreButtons(){
            StopRecording();if(Preview){Model.Notice="界面预览不会写入设备。";return;}
            if(!Model.CanRestoreButtons)return;Model.Notice="正在恢复默认按键…";
            if(await SendSetting((session,cancel)=>MacroProtocol.Restore(session,cancel))){
                foreach(var macro in Model.Macros.Library)macro.TargetIndex=0;
                Model.Macros.Status="已发送默认按键";Model.Notice="默认按键方案已发送，已解除本机宏方案的绑定。";
            }
        }
        void SaveMacros(){
            StopRecording();operationUntil=DateTime.UtcNow.AddSeconds(20);
            if(Preview){Model.Notice="界面预览不会保存设置。";return;}
            try{MacroStorage.Save(MacroStorage.DefaultPath,Model.Macros.Library);Model.Macros.Status="本机已保存 · 不自动应用";Model.Notice="宏草稿已保存到本机，不会自动绑定或运行。";}catch(Exception ex){Model.Notice="保存失败："+ex.Message;}
        }
        void SaveSensitivity(){
            operationUntil=DateTime.UtcNow.AddSeconds(15);
            if(Preview){Model.Notice="界面预览不会保存设置。";return;}
            if(!Model.Sensitivity.Valid){Model.Notice=Model.Sensitivity.Error;return;}
            try{
                var draft=Model.Sensitivity.Draft();
                using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath+@"\Sensitivity")){
                    key.SetValue("values",String.Join(",",draft.Values));key.SetValue("selected",draft.Selected);key.SetValue("rate",Model.Sensitivity.PollingIndex);
                }
                Model.Sensitivity.Status="本机已保存 · 尚未应用";Model.Notice="DPI 与回报率草稿已保存；启动时不会自动应用。";
            }catch(Exception ex){Model.Notice="保存失败："+ex.Message;}
        }
        void SaveDraft() {
            operationUntil=DateTime.UtcNow.AddSeconds(15);
            if(!Model.ValidColor){Model.Notice="请输入有效的 #RRGGBB 颜色。";return;}
            if(Preview){Model.Notice="界面预览不会保存设置。";return;}
            try {
                Lighting d=Model.Draft();
                using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)) {
                    key.SetValue("mode",d.Mode);key.SetValue("brightness",d.Brightness);key.SetValue("color",d.Hex);
                    key.SetValue("enabled",d.Enabled?1:0);
                }
                Model.DraftStatus="本机已保存";Model.Notice="草稿已保存到本机；启动时不会自动应用到鼠标。";
            } catch(Exception ex){Model.Notice="保存失败："+ex.Message;}
        }
        void LoadDraft() {
            try{Model.Macros.Replace(MacroStorage.LoadForEditor(MacroStorage.DefaultPath));}
            catch(Exception){Model.Macros.Status="本机宏文件不可读；原文件未修改";}
            try{using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath+@"\Sensitivity")){
                if(key!=null){
                    string[] saved=Convert.ToString(key.GetValue("values","")).Split(',');
                    int[] values=Array.ConvertAll(saved,value=>Int32.Parse(value,System.Globalization.CultureInfo.InvariantCulture));
                    Model.Sensitivity.Load(new DpiScheme {Values=values,Selected=Convert.ToInt32(key.GetValue("selected",1))});
                    Model.Sensitivity.PollingIndex=Convert.ToInt32(key.GetValue("rate",2));
                }
            }}catch(Exception){Model.Sensitivity.Status="草稿不可读 · 已恢复默认方案";}
            try {using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath)) {
                if(key==null)return;
                Model.ModeIndex=Math.Max(0,Math.Min(7,Convert.ToInt32(key.GetValue("mode",1))-1));
                Model.Brightness=Convert.ToInt32(key.GetValue("brightness",8));
                Model.ColorHex=Convert.ToString(key.GetValue("color","#46D3FF"));
                if(!Model.ValidColor)Model.ColorHex="#46D3FF";
                Model.LightEnabled=Convert.ToInt32(key.GetValue("enabled",1))!=0;
                Model.DraftStatus="本机草稿";
            }}catch(Exception){Model.Notice="本机草稿不可读，已使用默认预览。";}
        }
        void PickColor() {
            using(var picker=new Forms.ColorDialog()) {
                picker.FullOpen=true;
                if(Model.ValidColor)picker.Color=System.Drawing.ColorTranslator.FromHtml(Model.ColorHex);
                if(picker.ShowDialog()==Forms.DialogResult.OK)Model.ColorHex=String.Format("#{0:X2}{1:X2}{2:X2}",picker.Color.R,picker.Color.G,picker.Color.B);
            }
        }
        static bool ReadAutostart() {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return key!=null&&key.GetValue(RunName)!=null;}
        void ChangeAutostart(bool enabled) {
            operationUntil=DateTime.UtcNow.AddSeconds(15);
            if(Preview){Model.Notice="界面预览不会修改开机设置。";return;}
            try {using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if(enabled)key.SetValue(RunName,"\""+Assembly.GetExecutingAssembly().Location+"\" --tray",RegistryValueKind.String);
                else key.DeleteValue(RunName,false);
            }Model.Notice=enabled?"已启用 GM GEAR 开机自启。":"已关闭 GM GEAR 开机自启。";}
            catch(Exception ex){((CheckBox)Window.FindName("AutostartCheck")).IsChecked=ReadAutostart();Model.Notice="无法更改开机设置："+ex.Message;}
        }
        void CreateTray() {
            tray=new Forms.NotifyIcon();tray.Text="GM GEAR · 等待鼠标电量";
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开 GM GEAR",null,(s,e)=>Show());menu.Items.Add("退出 GM GEAR",null,(s,e)=>Exit());
            tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Show();UpdateTray(null);tray.Visible=true;
        }
        void UpdateTray(int? level) {
            if(tray==null)return;
            using(var bitmap=new System.Drawing.Bitmap(32,32))
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;graphics.Clear(System.Drawing.Color.Transparent);
                using(var track=new System.Drawing.Pen(System.Drawing.Color.FromArgb(85,100,120),3))graphics.DrawEllipse(track,3,3,26,26);
                if(level.HasValue)using(var pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(79,213,255),3))graphics.DrawArc(pen,3,3,26,26,-90,level.Value*3.6f);
                using(var font=new System.Drawing.Font("Segoe UI",9,System.Drawing.FontStyle.Bold))
                using(var brush=new System.Drawing.SolidBrush(System.Drawing.Color.White)) {
                    var format=new System.Drawing.StringFormat {Alignment=System.Drawing.StringAlignment.Center,LineAlignment=System.Drawing.StringAlignment.Center};
                    graphics.DrawString(level.HasValue?level.Value.ToString():"?",font,brush,new System.Drawing.RectangleF(0,0,32,32),format);format.Dispose();
                }
                IntPtr raw=bitmap.GetHicon();System.Drawing.Icon icon;
                using(var borrowed=System.Drawing.Icon.FromHandle(raw))icon=(System.Drawing.Icon)borrowed.Clone();
                DestroyIcon(raw);tray.Icon=icon;var old=trayIcon;trayIcon=icon;if(old!=null)old.Dispose();
            }
            tray.Text="GM GEAR · GM3323D："+(level.HasValue?level.Value+"%":"未知");
        }
        void SetupNativeWindow() {
            IntPtr hwnd=new WindowInteropHelper(Window).Handle;
            int dark=1;DwmSetWindowAttribute(hwnd,20,ref dark,4);
            int caption=0x201B19;DwmSetWindowAttribute(hwnd,35,ref caption,4);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled)=>{
                if(msg==OpenMessage){Show();handled=true;}return IntPtr.Zero;
            });
        }
        internal void Show() {Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();}
        internal void Exit() {exiting=true;Window.Close();}
        public void Dispose() {if(disposed)return;StopRecording();disposed=true;monitor.Dispose();if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();}if(trayIcon!=null)trayIcon.Dispose();}
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int a,ref int value,int size);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        internal static readonly int OpenMessage=RegisterWindowMessage("GM3323DGearHub_Open");
    }

    internal static class Program {
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int msg,IntPtr wp,IntPtr lp);
        [STAThread] public static int Main(string[] args) {
            bool first;using(var mutex=new Mutex(true,"Local\\GM3323DGearHub.Desktop",out first)) {
                if(!first){IntPtr existing=FindWindow(null,"GM GEAR · GM3323D");if(existing!=IntPtr.Zero)PostMessage(existing,Hub.OpenMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
                SetCurrentProcessExplicitAppUserModelID("GM3323D.GearHub.Desktop");
                var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
                app.DispatcherUnhandledException+=(s,e)=>{Forms.MessageBox.Show(e.Exception.Message,"GM GEAR");e.Handled=true;};
                try {using(var hub=new Hub(false)) {
                    app.MainWindow=hub.Window;
                    if(Array.IndexOf(args,"--dpi")>=0)hub.Route("Dpi");
                    if(Array.IndexOf(args,"--macros")>=0)hub.Route("Buttons");
                    if(Array.IndexOf(args,"--tray")<0)hub.Window.Show();
                    else {var handle=new WindowInteropHelper(hub.Window).EnsureHandle();}
                    return app.Run();
                }}catch(Exception ex){Forms.MessageBox.Show(ex.Message,"GM GEAR 启动失败");return 1;}
            }
        }
    }
}
