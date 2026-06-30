using System.Runtime.InteropServices;

namespace VL.DynamicShader.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MyStruct
    {

        public float Test;
        public MyStruct(float test)
        {
            Test = test;
        }
    }
}
