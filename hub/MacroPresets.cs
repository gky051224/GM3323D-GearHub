using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace GearHub {
    public sealed class MacroPreset {
        readonly Key[] keys;
        public string Id {get;private set;}
        public string Label {get;private set;}
        public override string ToString(){return Label;}
        internal MacroPreset(string id,string name,string shortcut,params Key[] chord){Id=id;Label=name+" · "+shortcut;keys=chord;}
        public MacroDefinition Create(bool initial){
            var macro=new MacroDefinition {Name=Label};if(initial)macro.Id="preset-"+Id;
            foreach(var key in keys)macro.Actions.Add(new MacroAction {Code=MacroKeys.FromWpf(key).Value,Down=true,DelayText="50"});
            for(int i=keys.Length-1;i>=0;i--)macro.Actions.Add(new MacroAction {Code=MacroKeys.FromWpf(keys[i]).Value,Down=false,DelayText="50"});
            return macro;
        }
    }
    public static class MacroPresets {
        static readonly IList<MacroPreset> presets=Array.AsReadOnly(new[]{
            new MacroPreset("copy","复制","Ctrl+C",Key.LeftCtrl,Key.C),
            new MacroPreset("paste","粘贴","Ctrl+V",Key.LeftCtrl,Key.V),
            new MacroPreset("cut","剪切","Ctrl+X",Key.LeftCtrl,Key.X),
            new MacroPreset("undo","撤销","Ctrl+Z",Key.LeftCtrl,Key.Z),
            new MacroPreset("redo","重做","Ctrl+Y",Key.LeftCtrl,Key.Y),
            new MacroPreset("select-all","全选","Ctrl+A",Key.LeftCtrl,Key.A),
            new MacroPreset("save","保存","Ctrl+S",Key.LeftCtrl,Key.S),
            new MacroPreset("find","查找","Ctrl+F",Key.LeftCtrl,Key.F),
            new MacroPreset("refresh","刷新","F5",Key.F5),
            new MacroPreset("rename","重命名","F2",Key.F2),
            new MacroPreset("new-tab","新建标签页","Ctrl+T",Key.LeftCtrl,Key.T),
            new MacroPreset("close-tab","关闭标签页","Ctrl+W",Key.LeftCtrl,Key.W),
            new MacroPreset("reopen-tab","重新打开标签页","Ctrl+Shift+T",Key.LeftCtrl,Key.LeftShift,Key.T),
            new MacroPreset("next-tab","下一标签页","Ctrl+Tab",Key.LeftCtrl,Key.Tab),
            new MacroPreset("previous-tab","上一标签页","Ctrl+Shift+Tab",Key.LeftCtrl,Key.LeftShift,Key.Tab),
            new MacroPreset("switch-window","切换窗口","Alt+Tab",Key.LeftAlt,Key.Tab),
            new MacroPreset("desktop","显示桌面","Win+D",Key.LWin,Key.D),
            new MacroPreset("explorer","文件资源管理器","Win+E",Key.LWin,Key.E),
            new MacroPreset("screenshot","区域截图","Win+Shift+S",Key.LWin,Key.LeftShift,Key.S),
            new MacroPreset("plain-paste","粘贴为纯文本","Ctrl+Shift+V",Key.LeftCtrl,Key.LeftShift,Key.V)
        });
        public static IList<MacroPreset> All {get{return presets;}}
        public static int AddDefaults(List<MacroDefinition> library){
            var additions=presets.Take(8).Where(p=>!library.Any(m=>m.Id=="preset-"+p.Id||m.Name==p.Label)).Take(Math.Max(0,20-library.Count)).Select(p=>p.Create(true)).ToList();
            library.InsertRange(0,additions);return additions.Count;
        }
    }
}
