// GM3323D protocol ported from Evan816Z/GM3323D-MouseLight (MIT, tongz 2026).
// Keep the 132-byte configuration intact except the five confirmed lighting fields.
using System;
using System.Threading;

namespace GearHub {
    public sealed class Lighting {
        public int Mode = 1, Brightness = 8;
        public byte R = 70, G = 211, B = 255;
        public bool Enabled = true;
        public bool ReportIdPrefix;
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
        byte[] Read();
        bool Write(byte[] bytes);
        void Cancel();
    }

    public static class Protocol {
        public static byte[] Patch(byte[] current, Lighting draft) {
            draft.Validate();
            if (current == null || current.Length != 132 || current[0] != 5 || current[1] != 13)
                throw new InvalidOperationException("未读到有效配置，未向鼠标写入。请打开原厂灯光页面后重试。");
            byte[] result = (byte[])current.Clone();
            result[3] = (byte)(draft.Enabled ? draft.Mode : 0);
            result[5] = (byte)draft.Brightness;
            result[6] = draft.R; result[7] = draft.G; result[8] = draft.B;
            if (draft.ReportIdPrefix) result[0] = 4;
            return result;
        }

        public static void Apply(IFeatureSession session, Lighting draft, CancellationToken cancel) {
            draft.Validate();
            byte[] current = null;
            for (int attempt=0; attempt<24; attempt++) {
                cancel.ThrowIfCancellationRequested();
                current = session.Read();
                if (current != null && current.Length==132 && current[0]==5 && current[1]==13) break;
                current = null;
                if (cancel.WaitHandle.WaitOne(60)) cancel.ThrowIfCancellationRequested();
            }
            byte[] packet = Patch(current, draft);
            cancel.ThrowIfCancellationRequested();
            if (!session.Write(packet)) throw new InvalidOperationException("设备未接受设置，未确认应用成功。");
        }
    }
}
