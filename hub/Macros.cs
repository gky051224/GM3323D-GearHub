using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Input;

namespace GearHub {
    public sealed class MacroKey {
        public string Name {get;set;} public byte Code {get;set;}
        public override string ToString(){return Name;}
    }
    public static class MacroKeys {
        static readonly List<MacroKey> keys=Create();
        static List<MacroKey> Create(){
            var list=new List<MacroKey>();
            for(int i=0;i<26;i++)list.Add(new MacroKey {Name=((char)('A'+i)).ToString(),Code=(byte)(4+i)});
            for(int i=1;i<=9;i++)list.Add(new MacroKey {Name=i.ToString(),Code=(byte)(29+i)});
            list.Add(new MacroKey {Name="0",Code=39});
            string[] special={"Enter","Esc","Backspace","Tab","Space","-","=","[","]","\\","#",";","'","`",",",".","/","Caps Lock"};
            for(int i=0;i<special.Length;i++)list.Add(new MacroKey {Name=special[i],Code=(byte)(40+i)});
            for(int i=0;i<24;i++)list.Add(new MacroKey {Name="F"+(i+1),Code=(byte)(i<12?58+i:104+i-12)});
            string[] nav={"Print Screen","Scroll Lock","Pause","Insert","Home","Page Up","Delete","End","Page Down","→","←","↓","↑"};
            for(int i=0;i<nav.Length;i++)list.Add(new MacroKey {Name=nav[i],Code=(byte)(70+i)});
            string[] modifiers={"左 Ctrl","左 Shift","左 Alt","左 Win","右 Ctrl","右 Shift","右 Alt","右 Win"};
            for(int i=0;i<8;i++)list.Add(new MacroKey {Name=modifiers[i],Code=(byte)(224+i)});
            return list;
        }
        public static IList<MacroKey> All {get{return keys.AsReadOnly();}}
        public static MacroKey Find(byte code){return keys.Find(k=>k.Code==code);}
        public static byte? FromWpf(Key key){
            if(key>=Key.A&&key<=Key.Z)return (byte)(4+key-Key.A);
            if(key>=Key.D1&&key<=Key.D9)return (byte)(30+key-Key.D1);
            if(key==Key.D0)return 39;
            if(key>=Key.F1&&key<=Key.F12)return (byte)(58+key-Key.F1);
            if(key>=Key.F13&&key<=Key.F24)return (byte)(104+key-Key.F13);
            var map=new Dictionary<Key,byte>{{Key.Return,40},{Key.Escape,41},{Key.Back,42},{Key.Tab,43},{Key.Space,44},{Key.OemMinus,45},{Key.OemPlus,46},{Key.OemOpenBrackets,47},{Key.OemCloseBrackets,48},{Key.OemPipe,49},{Key.OemSemicolon,51},{Key.OemQuotes,52},{Key.OemTilde,53},{Key.OemComma,54},{Key.OemPeriod,55},{Key.OemQuestion,56},{Key.Capital,57},{Key.Insert,73},{Key.Home,74},{Key.PageUp,75},{Key.Delete,76},{Key.End,77},{Key.PageDown,78},{Key.Right,79},{Key.Left,80},{Key.Down,81},{Key.Up,82},{Key.LeftCtrl,224},{Key.LeftShift,225},{Key.LeftAlt,226},{Key.LWin,227},{Key.RightCtrl,228},{Key.RightShift,229},{Key.RightAlt,230},{Key.RWin,231}};
            byte code;return map.TryGetValue(key,out code)?(byte?)code:null;
        }
    }
    public sealed class MacroAction : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        string delay="50";
        public byte Code {get;set;} public bool Down {get;set;}
        public string KeyName {get{var key=MacroKeys.Find(Code);return key==null?"未知按键":key.Name;}}
        public string ActionName {get{return Down?"按下":"松开";}}
        public string DelayText {get{return delay;}set{delay=value??"";var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs("DelayText"));}}
        public int Delay {get{int value;if(!Int32.TryParse(delay,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)||value<0||value>10000||value%2!=0)throw new ArgumentException("延迟须为 0–10000 ms 的偶数。");return value;}}
        public MacroAction Clone(){return new MacroAction {Code=Code,Down=Down,DelayText=DelayText};}
    }
    public sealed class MacroDefinition : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        string name="新建宏",repeat="1";int target;
        public string Id {get;set;}
        public ObservableCollection<MacroAction> Actions {get;private set;}
        public MacroDefinition(){Id=Guid.NewGuid().ToString("N");Actions=new ObservableCollection<MacroAction>();Actions.CollectionChanged+=(s,e)=>{if(e.NewItems!=null)foreach(MacroAction action in e.NewItems)action.PropertyChanged+=(a,b)=>Changed("Actions");Changed("Actions");};}
        void Changed(string field){var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs(field));}
        public string Name {get{return name;}set{name=value??"";Changed("Name");}}
        public string RepeatText {get{return repeat;}set{repeat=value??"";Changed("RepeatText");}}
        public int Repeats {get{int value;if(!Int32.TryParse(repeat,out value)||value<1||value>255)throw new ArgumentException("重复次数须为 1–255。");return value;}}
        // Index 0 is deliberately unassigned. Actual physical positions are 3,4,5.
        public int TargetIndex {get{return target;}set{if(value<0||value>3)return;target=value;Changed("TargetIndex");}}
        public int Button {get{if(target==0)throw new ArgumentException("请先选择触发按键；新宏默认未绑定。");return target+2;}}
        public MacroDefinition Clone(){var m=new MacroDefinition {Id=Id,Name=Name,RepeatText=RepeatText,TargetIndex=TargetIndex};foreach(var a in Actions)m.Actions.Add(a.Clone());return m;}
    }
    public sealed class MacroModel : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<MacroDefinition> Library {get;private set;}
        MacroDefinition selected;MacroAction action;MacroKey key=MacroKeys.All[0];MacroPreset preset=MacroPresets.All[0];int insertMode;
        string delay="50",status="本机草稿 · 默认未绑定";bool recording;
        public MacroModel(){Library=new ObservableCollection<MacroDefinition>();Library.CollectionChanged+=(s,e)=>{if(e.NewItems!=null)foreach(MacroDefinition m in e.NewItems)m.PropertyChanged+=(a,b)=>Dirty();Dirty();};var initial=new List<MacroDefinition>();MacroPresets.AddDefaults(initial);Replace(initial);}
        void Notify(string field){var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs(field));}
        void Dirty(){Status="本机草稿 · 尚未应用";Notify("Error");Notify("CanApply");Notify("StepCount");}
        public MacroDefinition Selected {get{return selected;}set{selected=value;Action=null;Notify("Selected");Notify("HasSelection");Notify("Error");Notify("StepCount");}}
        public bool HasSelection {get{return selected!=null;}}
        public MacroAction Action {get{return action;}set{action=value;Notify("Action");}}
        public IList<MacroKey> Keys {get{return MacroKeys.All;}}
        public IList<MacroPreset> Presets {get{return MacroPresets.All;}}
        public MacroPreset SelectedPreset {get{return preset;}set{preset=value;Notify("SelectedPreset");}}
        public MacroKey InsertKey {get{return key;}set{key=value;Notify("InsertKey");}}
        public string[] InsertModes {get{return new[]{"点按（按下 + 松开）","仅按下","仅松开"};}}
        public int InsertMode {get{return insertMode;}set{insertMode=value;Notify("InsertMode");}}
        public string InsertDelay {get{return delay;}set{delay=value??"";Notify("InsertDelay");}}
        public string[] Targets {get{return new[]{"未绑定","后侧键（后退）","前侧键（前进）","顶部 DPI 键"};}}
        public string Status {get{return status;}set{status=value;Notify("Status");}}
        public bool Recording {get{return recording;}set{recording=value;Notify("RecordLabel");Notify("Recording");Notify("CanApply");}}
        public string RecordLabel {get{return recording?"停止录制（Esc）":"录制键盘";}}
        public string StepCount {get{return selected==null?"0 个动作":selected.Actions.Count+" 个动作";}}
        public string Error {get{if(selected==null||(selected.Actions.Count==0&&selected.TargetIndex==0))return "";try{MacroProtocol.ValidateDefinition(selected,false);if(Library.Any(m=>m.TargetIndex!=0))MacroProtocol.ValidatePlan(Library);return "";}catch(Exception ex){return ex.Message;}}}
        public bool CanApply {get{if(recording)return false;try{MacroProtocol.ValidatePlan(Library);return true;}catch(Exception){return false;}}}
        public void Add(){if(Library.Count>=20)throw new ArgumentException("最多保存 20 个本机宏。");var macro=new MacroDefinition {Name="新建宏 "+(Library.Count+1)};Library.Add(macro);Selected=macro;}
        public void AddPreset(){if(preset==null)return;if(Library.Count>=20)throw new ArgumentException("宏列表已满（20 个）；请先删除不需要的草稿。");var macro=preset.Create(false);Library.Add(macro);Selected=macro;}
        public void Remove(){if(selected==null)return;int index=Library.IndexOf(selected);Library.Remove(selected);Selected=Library.Count==0?null:Library[Math.Min(index,Library.Count-1)];}
        public void Insert(){
            if(selected==null||key==null)return;
            var probe=new MacroAction {Code=key.Code,DelayText=delay};int value=probe.Delay;
            if(selected.Actions.Count+(insertMode==0?2:1)>50)throw new ArgumentException("最多 50 个动作；长延迟会额外占用空间。");
            if(insertMode!=2)selected.Actions.Add(new MacroAction {Code=key.Code,Down=true,DelayText=value.ToString()});
            if(insertMode!=1)selected.Actions.Add(new MacroAction {Code=key.Code,Down=false,DelayText=value.ToString()});
        }
        public void Move(int delta){if(selected==null||action==null)return;int from=selected.Actions.IndexOf(action),to=from+delta;if(to>=0&&to<selected.Actions.Count)selected.Actions.Move(from,to);}
        public void DeleteAction(){if(selected!=null&&action!=null)selected.Actions.Remove(action);}
        public List<MacroDefinition> Snapshot(){return Library.Select(m=>m.Clone()).ToList();}
        public void Replace(IEnumerable<MacroDefinition> macros){Library.Clear();foreach(var m in macros)Library.Add(m);Selected=Library.FirstOrDefault();Status="本机已载入 · 不自动绑定";}
    }

    public sealed class MacroFile {
        public int Version {get;set;} public int PresetsVersion {get;set;} public List<MacroItem> Macros {get;set;}
    }
    public sealed class MacroItem {
        public string Id {get;set;} public string Name {get;set;} public string Repeat {get;set;} public int Target {get;set;}
        public List<MacroEvent> Actions {get;set;}
    }
    public sealed class MacroEvent {public byte Key {get;set;}public bool Down {get;set;}public string Delay {get;set;}}
    public static class MacroStorage {
        public static string DefaultPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"GM3323DGearHub\macros.json");}}
        public static void Save(string path,IEnumerable<MacroDefinition> library){
            var list=library.ToList();if(list.Count>20||list.Any(m=>m.Name.Length>40||m.Actions.Count>50))throw new ArgumentException("宏名称最多 40 字，单宏最多 50 个动作。");
            var file=new MacroFile {Version=1,PresetsVersion=1,Macros=list.Select(m=>new MacroItem {Id=m.Id,Name=m.Name,Repeat=m.RepeatText,Target=m.TargetIndex,Actions=m.Actions.Select(a=>new MacroEvent {Key=a.Code,Down=a.Down,Delay=a.DelayText}).ToList()}).ToList()};
            string json=new JavaScriptSerializer().Serialize(file);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+".tmp";File.WriteAllText(temp,json,System.Text.Encoding.UTF8);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static List<MacroDefinition> Load(string path){bool legacy;return Load(path,out legacy);}
        static List<MacroDefinition> Load(string path,out bool legacy){
            if(new FileInfo(path).Length>131072)throw new ArgumentException("宏文件过大。");
            var file=new JavaScriptSerializer().Deserialize<MacroFile>(File.ReadAllText(path));
            if(file==null||file.Version!=1||file.Macros==null||file.Macros.Count>20)throw new ArgumentException("宏文件版本或格式不正确。");
            legacy=file.PresetsVersion<1;
            var result=new List<MacroDefinition>();
            foreach(var m in file.Macros){
                if(m==null||m.Name==null||m.Name.Length>40||m.Actions==null||m.Actions.Count>50||m.Target<0||m.Target>3)throw new ArgumentException("宏文件内容不完整。");
                var macro=new MacroDefinition {Id=m.Id,Name=m.Name,RepeatText=m.Repeat??"1",TargetIndex=m.Target};
                foreach(var a in m.Actions){if(a==null||MacroKeys.Find(a.Key)==null||a.Delay==null||a.Delay.Length>16)throw new ArgumentException("宏动作格式不正确。");macro.Actions.Add(new MacroAction {Code=a.Key,Down=a.Down,DelayText=a.Delay});}
                result.Add(macro);
            }
            return result;
        }
        // Upgrade old drafts once. Saving the marker also preserves deliberate preset deletions.
        public static List<MacroDefinition> LoadForEditor(string path){
            bool exists=File.Exists(path),legacy=true;var library=exists?Load(path,out legacy):new List<MacroDefinition>();
            if(legacy){
                MacroPresets.AddDefaults(library);
                if(exists)File.Copy(path,path+".before-presets-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".bak");
                Save(path,library);
            }
            return library;
        }
    }
}
