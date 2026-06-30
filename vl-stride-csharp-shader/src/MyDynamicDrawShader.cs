using Stride.Rendering;
using VL.Core;
using VL.Core.Import;

namespace VL.DynamicShader
{
    [ProcessNode(HasStateOutput = true)]
    public class MyDynamicDrawShader : DynamicDrawShader
    {
        // Name of the shader we are going to use.
        protected override string ShaderName => "MyDraw_DrawFX";

        // Place we are goint to store a struct
        private MyStruct _myStruct = new MyStruct(1.0f);

        // Set's our an input from upstream patch.
        public void SetMyStructValue(MyStruct myStruct)
        {
            _myStruct = myStruct;
        }

        // Constructor takes NodeContext and passes it to base class.
        public MyDynamicDrawShader([Pin(Visibility = Model.PinVisibility.Hidden)] NodeContext nodeContext) : base(nodeContext)
        {
        }

        // Apply parameters to the shader effect.
        protected override void ApplyParameters(ParameterCollection parameters)
        {
            // MyShaderBaseKeys.MyStructInput is a ValueParameterKey<MyStruct> defined in MyShaderBase.sdsl.cs
            parameters.Set(MyShaderBaseKeys.MyStructInput, ref _myStruct);
        }
    }
}
