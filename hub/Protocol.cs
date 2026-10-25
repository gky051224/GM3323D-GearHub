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
            cancel.ThrowIfCancellationRequested();
            if (!session.Write(packet)) throw new InvalidOperationException("设备未接受设置，未确认应用成功。");
        }
    }
}
