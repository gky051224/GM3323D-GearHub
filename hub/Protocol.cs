// GM3323D battery support began with Evan816Z/GM3323D-MouseLight (MIT, tongz 2026).
// Lighting is an independent 13-byte command, verified on the user's receiver.
using System;
using System.Threading;

namespace GearHub {
    public sealed class Lighting {
        public int Mode = 1, Brightness = 8;
        public byte R = 70, G = 211, B = 255;
        public bool Enabled = true;
        public Lighting Clone() { return (Lighting)MemberwiseClone(); }
        public string Hex { get { return String.Format("#{0:X2}{1:X2}{2:X2}", R,G,B); } }
        public void Validate() {
            if (Mode < 1 || Mode > 8 || Brightness < 1 || Brightness > 8)
                throw new ArgumentException("灯效或亮度超出范围。");
        }
    }

    public sealed class BatterySample {
        public bool Recognized, Online;
        public int? Level;
        public static BatterySample Parse(byte[] b) {
            var r = new BatterySample();
            if (b == null || b.Length != 5 || b[0] != 3 || b[2] != 0x40 || (b[1] != 0 && b[1] != 1)) return r;
            r.Recognized = true; r.Online = b[3] != 0;
            if (r.Online && b[4] >= 1 && b[4] <= 10) r.Level = b[4] * 10;
            return r;
        }
    }

    public interface IFeatureSession : IDisposable {
        bool Write(byte[] bytes);
        void Cancel();
    }

    public static class Protocol {
        // Sensor register encodings indexed by units of 100 DPI, from the device protocol.
        static readonly byte[] Sensor={0,4,4,6,8,11,13,15,18,20,22,25,27,29,32,34,36,39,41,43,46,48,50,52,55,57,59,62,64,66,69,71,73,76,78,80,83,85,87,90,92,94,97,99,101,104,106,108,111,113,115};
        public static byte[] DpiCommand(DpiScheme draft){
            draft.Validate();var packet=new byte[50];
            packet[0]=4;packet[1]=50;packet[2]=1;packet[3]=8;packet[4]=8;packet[5]=0x3F;
            for(int i=0;i<draft.Values.Length;i++){
                int unit=draft.Values[i]/100;
                if(unit>50){packet[6]|=(byte)(1<<i);packet[7]|=(byte)(1<<i);unit/=2;}
                packet[8+i]=Sensor[unit];packet[16+i]=Sensor[unit];
            }
            packet[24]=(byte)(draft.Selected+1);
            byte[] colors={255,0,0, 0,255,0, 0,0,255, 0,255,255, 255,255,0, 255,0,255, 255,255,255, 255,255,255};
            Array.Copy(colors,0,packet,25,colors.Length);packet[49]=1;return packet;
        }
        public static byte[] PollingCommand(int rate){
            if(rate!=125&&rate!=250&&rate!=500&&rate!=1000)throw new ArgumentException("请选择 125、250、500 或 1000 Hz。");
            return new byte[]{6,9,1,(byte)(1000/rate),0,0,0,0,0};
        }
        public static int? ParseDpiStage(byte[] packet){
            if(packet!=null&&packet.Length==5&&packet[0]==3&&packet[1]==0&&packet[2]==0x10&&packet[3]>=1&&packet[3]<=8&&packet[4]==0)return packet[3]-1;
            return null;
        }
        public static void Send(IFeatureSession session,byte[] packet,CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();
            if(!session.Write(packet))throw new InvalidOperationException("设备未接受设置，未确认应用成功。");
        }
        public static byte[] DefaultProfileCommand(){return new byte[]{12,4,1,5};}
        static void SendDefaultProfileSetting(IFeatureSession session,byte[] packet,CancellationToken cancel){
            Send(session,DefaultProfileCommand(),cancel);
            // Match the device's configuration activation delay before writing its settings.
            if(cancel.WaitHandle.WaitOne(550))cancel.ThrowIfCancellationRequested();
            Send(session,packet,cancel);
        }
        public static void ApplyDpi(IFeatureSession session,DpiScheme draft,CancellationToken cancel){
            byte[] packet=DpiCommand(draft);SendDefaultProfileSetting(session,packet,cancel);
        }
        public static void ApplyPolling(IFeatureSession session,int rate,CancellationToken cancel){
            byte[] packet=PollingCommand(rate);SendDefaultProfileSetting(session,packet,cancel);
        }
        public static byte[] LightingCommand(Lighting draft) {
            draft.Validate();
            // Report 0x05: length 13, profile 1, mode, effect timing,
            // brightness, RGB, breathing timing, selector 1, two reserved zeros.
            // The timing values are fixed defaults; no DPI/button/profile data is sent.
            return new byte[] { 5, 13, 1, (byte)(draft.Enabled ? draft.Mode : 0),
                4, (byte)draft.Brightness, draft.R, draft.G, draft.B, 6, 1, 0, 0 };
        }

        public static void Apply(IFeatureSession session, Lighting draft, CancellationToken cancel) {
            byte[] packet = LightingCommand(draft);
            Send(session,packet,cancel);
        }
    }
}
