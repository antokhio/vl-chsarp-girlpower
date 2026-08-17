using Stride.Core.Mathematics;

namespace VL.ShaderFXExt
{
    public struct MyStruct
    {
        Vector4 Color;

        public MyStruct(Color color)
        {
            Color = color.ToVector4();
        }
    }
}
