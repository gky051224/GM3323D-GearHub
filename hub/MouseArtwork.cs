using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace GearHub {
    internal static class MouseArtwork {
        static ImageSource image;
        public static ImageSource Image {get {
            if(image==null)using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("GearHub.MouseProduct")){
                var bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                // Fit the actual product rather than relying on offsets from a previous render.
                // Crop only in WPF memory; the original PNG and its alpha remain unchanged.
                var rgba=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);
                int width=rgba.PixelWidth,height=rgba.PixelHeight,stride=width*4;
                byte[] pixels=new byte[stride*height];rgba.CopyPixels(pixels,stride,0);
                int left=width,top=height,right=-1,bottom=-1;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                    if(pixels[y*stride+x*4+3]<16)continue;
                    left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);
                }
                if(right>=left&&bottom>=top){
                    left=Math.Max(0,left-8);top=Math.Max(0,top-8);right=Math.Min(width-1,right+8);bottom=Math.Min(height-1,bottom+8);
                    var cropped=new CroppedBitmap(bitmap,new Int32Rect(left,top,right-left+1,bottom-top+1));cropped.Freeze();image=cropped;
                }else{bitmap.Freeze();image=bitmap;}
            }
            return image;
        }}
    }

    // Color only the neutral-white illumination; keep the photographed shell unchanged.
    public sealed class RgbLightEffect : ShaderEffect {
        public static readonly DependencyProperty InputProperty=RegisterPixelShaderSamplerProperty("Input",typeof(RgbLightEffect),0);
        public static readonly DependencyProperty TintProperty=DependencyProperty.Register("Tint",typeof(Color),typeof(RgbLightEffect),new UIPropertyMetadata(Colors.Cyan,PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty PowerProperty=DependencyProperty.Register("Power",typeof(double),typeof(RgbLightEffect),new UIPropertyMetadata(1.0,PixelShaderConstantCallback(1)));
        public Brush Input {get{return (Brush)GetValue(InputProperty);}set{SetValue(InputProperty,value);}}
        public Color Tint {get{return (Color)GetValue(TintProperty);}set{SetValue(TintProperty,value);}}
        public double Power {get{return (double)GetValue(PowerProperty);}set{SetValue(PowerProperty,value);}}
        static PixelShader shader;
        public RgbLightEffect() {
            if(shader==null)shader=Compile();PixelShader=shader;
            UpdateShaderValue(InputProperty);UpdateShaderValue(TintProperty);UpdateShaderValue(PowerProperty);
        }
        const string Source=@"sampler2D image : register(s0);
float4 tint : register(c0);
float power : register(c1);
float4 main(float2 uv : TEXCOORD) : COLOR {
    float4 p=tex2D(image,uv);
    float3 rgb=p.rgb/max(p.a,0.0001);
    float mask=step(0.55,uv.y)*smoothstep(0.55,0.9,min(rgb.r,min(rgb.g,rgb.b)));
    float3 output=lerp(rgb,rgb*tint.rgb*power,mask);
    return float4(output*p.a,p.a);
}";
        [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Ansi)]
        static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)]string source,UIntPtr size,string name,IntPtr defines,IntPtr include,string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate IntPtr BufferPointer(IntPtr blob);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate UIntPtr BufferSize(IntPtr blob);
        static PixelShader Compile() {
            IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
            try {
                int result=D3DCompile(Source,new UIntPtr((uint)Encoding.ASCII.GetByteCount(Source)),"GMGEAR-RGB",IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",0,0,out code,out errors);
                if(result<0||code==IntPtr.Zero)throw new InvalidOperationException("RGB preview shader could not be loaded");
                IntPtr table=Marshal.ReadIntPtr(code);
                var pointer=(BufferPointer)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table,3*IntPtr.Size),typeof(BufferPointer));
                var size=(BufferSize)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table,4*IntPtr.Size),typeof(BufferSize));
                byte[] bytes=new byte[checked((int)size(code).ToUInt64())];Marshal.Copy(pointer(code),bytes,0,bytes.Length);
                var value=new PixelShader();using(var stream=new MemoryStream(bytes))value.SetStreamSource(stream);value.Freeze();return value;
            } finally {if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
        }
    }
}
