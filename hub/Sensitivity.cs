using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace GearHub {
    public sealed class DpiScheme {
        public int[] Values={800,1200,1600,2400,3200,5000};
        public int Selected=1;
        public static bool ValidValue(int value) {
            return value>=200&&value<=10000&&value%(value>5000?200:100)==0;
        }
        public void Validate() {
            if(Values==null||Values.Length!=6||Selected<0||Selected>=Values.Length||!Values.All(ValidValue))
                throw new ArgumentException("DPI 应为 200–5000 的 100 整数倍，或 5200–10000 的 200 整数倍。");
        }
        public DpiScheme Clone(){return new DpiScheme {Values=(int[])Values.Clone(),Selected=Selected};}
    }

    public sealed class DpiStage : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        int value;bool selected;
        public int Index {get;private set;}
        public string Name {get{return "档位 "+(Index+1);}}
        public int Value {get{return value;}set{if(this.value==value)return;this.value=value;Changed("Value");}}
        public bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Changed("Selected");}}
        public DpiStage(int index,int dpi){Index=index;value=dpi;}
        void Changed(string name){var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs(name));}
    }

    public sealed class SensitivityModel : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<DpiStage> Stages {get;private set;}
        int selected=1,polling=2;string text="1200",status="本机方案 · 尚未应用",rateStatus="尚未应用",hardware="等待鼠标档位事件";
        bool changing;int[] lastSent;
        public SensitivityModel(){
            Stages=new ObservableCollection<DpiStage>();
            var initial=new DpiScheme();
            for(int i=0;i<initial.Values.Length;i++){
                var stage=new DpiStage(i,initial.Values[i]);stage.Selected=i==selected;
                stage.PropertyChanged+=StageChanged;Stages.Add(stage);
            }
        }
        void Notify(string name){var fn=PropertyChanged;if(fn!=null)fn(this,new PropertyChangedEventArgs(name));}
        void StageChanged(object sender,PropertyChangedEventArgs e){
            if(changing)return;var stage=(DpiStage)sender;
            if(e.PropertyName=="Selected"&&stage.Selected){Select(stage.Index);return;}
            if(e.PropertyName=="Value")Dirty();
        }
        void Dirty(){Status="本机方案 · 尚未应用";Notify("Valid");}
        public int SelectedIndex {get{return selected;}}
        public string SelectedLabel {get{return "选中档位 "+(selected+1);}}
        public string EditText {get{return text;}set{
            text=value??"";int parsed;
            if(Int32.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out parsed)&&DpiScheme.ValidValue(parsed)){
                Stages[selected].Value=parsed;Notify("SliderValue");
            }
            Notify("EditText");Notify("Error");Dirty();
        }}
        public bool Valid {get{int v;return Int32.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out v)&&DpiScheme.ValidValue(v);}}
        public string Error {get{return Valid?"":"请输入 200–5000（步进 100），或 5200–10000（步进 200）。";}}
        public double SliderValue {get{return Stages[selected].Value;}set{
            double step=value>5000?200:100;
            EditText=Math.Max(200,Math.Min(10000,(int)(Math.Round(value/step)*step))).ToString(CultureInfo.InvariantCulture);
        }}
        public string Status {get{return status;}set{status=value;Notify("Status");}}
        public string Hardware {get{return hardware;}set{hardware=value;Notify("Hardware");}}
        public int[] PollingRates {get{return new[]{125,250,500,1000};}}
        public int PollingIndex {get{return polling;}set{if(value<0||value>3)return;polling=value;RateStatus="本机方案 · 尚未应用";Notify("PollingIndex");}}
        public int PollingRate {get{return PollingRates[polling];}}
        public string RateStatus {get{return rateStatus;}set{rateStatus=value;Notify("RateStatus");}}
        public void Select(int index){
            if(index<0||index>=Stages.Count)return;
            changing=true;selected=index;
            foreach(var stage in Stages)stage.Selected=stage.Index==index;
            changing=false;text=Stages[index].Value.ToString(CultureInfo.InvariantCulture);
            Notify("EditText");Notify("SliderValue");Notify("SelectedIndex");Notify("SelectedLabel");Notify("Error");Dirty();
        }
        public DpiScheme Draft(){
            if(!Valid)throw new ArgumentException(Error);
            var scheme=new DpiScheme {Values=Stages.Select(s=>s.Value).ToArray(),Selected=selected};scheme.Validate();return scheme;
        }
        public void Load(DpiScheme scheme){
            scheme.Validate();changing=true;
            for(int i=0;i<Stages.Count;i++)Stages[i].Value=scheme.Values[i];
            changing=false;Select(scheme.Selected);Status="本机草稿 · 尚未应用";
        }
        public void Sent(DpiScheme scheme){
            lastSent=(int[])scheme.Values.Clone();Status="已发送 "+scheme.Values[scheme.Selected]+" DPI";
            Hardware="已请求档位 "+(scheme.Selected+1)+"，等待鼠标回报";
        }
        public void ObserveStage(int index){
            Hardware="鼠标回报：档位 "+(index+1)+(lastSent!=null&&index<lastSent.Length?" · "+lastSent[index]+" DPI（本次已发送方案）":" · 数值未回读");
        }
        public void InvalidateDevice(){lastSent=null;Hardware="连接已中断，等待鼠标档位事件";}
    }
}
