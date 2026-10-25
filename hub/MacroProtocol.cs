using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GearHub {
    public static class MacroProtocol {
        public static void ValidateDefinition(MacroDefinition macro,bool binding){
            if(macro==null||String.IsNullOrWhiteSpace(macro.Name)||macro.Name.Length>40)throw new ArgumentException("请输入 1–40 字的宏名称。");
            if(binding){int button=macro.Button;}
            int repeat=macro.Repeats;
            if(macro.Actions.Count<2||macro.Actions.Count>50)throw new ArgumentException("请添加成对的按下与松开动作，最多 50 个。");
            var held=new HashSet<byte>();int slots=0,extensions=0;
            foreach(var action in macro.Actions){
                if(MacroKeys.Find(action.Code)==null)throw new ArgumentException("包含不支持的按键。");
                if(action.Down){if(!held.Add(action.Code))throw new ArgumentException("同一按键重复按下，请补上松开动作。");}
                else if(!held.Remove(action.Code))throw new ArgumentException("存在未先按下的松开动作。");
                slots++;if(action.Delay>254){slots++;extensions++;}
            }
            if(held.Count>0)throw new ArgumentException("宏结束时仍有按键按下，请添加对应松开动作。");
            if(slots>50||extensions>20)throw new ArgumentException("宏超出鼠标容量；请减少动作或长延迟。");
        }
        public static void ValidatePlan(IEnumerable<MacroDefinition> source){
            var bound=source.Where(m=>m.TargetIndex!=0).ToList();
            if(bound.Count==0)throw new ArgumentException("尚未绑定；选择触发键后才能应用。");
            if(bound.Select(m=>m.Button).Distinct().Count()!=bound.Count)throw new ArgumentException("同一个触发键只能分配一个宏。");
            foreach(var macro in bound)ValidateDefinition(macro,true);
        }
        public static byte[] MacroCommand(MacroDefinition macro){
            ValidateDefinition(macro,true);var bytes=new byte[129];bytes[0]=9;bytes[1]=129;bytes[2]=(byte)(macro.Button+1);
            bytes[3]=0;bytes[7]=(byte)macro.Repeats;int slot=0;
            foreach(var action in macro.Actions){
                int delay=action.Delay;int units=delay>254?delay/200:0;int remainder=delay-units*200;
                bytes[29+slot*2]=(byte)((remainder/2)|(action.Down?0:0x80));bytes[30+slot*2]=action.Code;slot++;
                if(units>0){bytes[29+slot*2]=(byte)units;bytes[30+slot*2]=3;slot++;}
            }
            bytes[28]=(byte)slot;return bytes;
        }
        public static byte[] ButtonCommand(IEnumerable<MacroDefinition> source){
            var bytes=new byte[77];bytes[0]=8;bytes[1]=77;bytes[2]=1;
            byte[] functions={2,3,4,5,6,13,14,15,0,0,9,10};
            for(int i=0;i<functions.Length;i++)bytes[3+i*3]=functions[i];
            foreach(var macro in source.Where(m=>m.TargetIndex!=0)){
                int offset=3+macro.Button*3;bytes[offset]=18;bytes[offset+1]=0;bytes[offset+2]=(byte)(macro.Button+1);
            }
            return bytes;
        }
        public static void Apply(IFeatureSession session,IEnumerable<MacroDefinition> source,CancellationToken cancel){
            var library=source.Select(m=>m.Clone()).ToList();ValidatePlan(library);
            var commands=library.Where(m=>m.TargetIndex!=0).Select(MacroCommand).ToList();
            byte[] mapping=ButtonCommand(library),defaults=ButtonCommand(new MacroDefinition[0]);
            Protocol.Send(session,Protocol.DefaultProfileCommand(),cancel);Pause(cancel);
            Protocol.Send(session,defaults,cancel);Pause(cancel);
            // Upload complete macros while their triggers are unassigned; bind only at the end.
            foreach(var command in commands){Protocol.Send(session,command,cancel);Pause(cancel);}
            Protocol.Send(session,mapping,cancel);
        }
        public static void Restore(IFeatureSession session,CancellationToken cancel){
            Protocol.Send(session,Protocol.DefaultProfileCommand(),cancel);Pause(cancel);
            Protocol.Send(session,ButtonCommand(new MacroDefinition[0]),cancel);
        }
        static void Pause(CancellationToken cancel){if(cancel.WaitHandle.WaitOne(550))cancel.ThrowIfCancellationRequested();}
    }
}
