# vl-stride-csharp-shader (PoC)

Shows how to run and manage your custom shader from C# using `VL.Stride.Runtime`.

### The Problem 

By default, VL compiles shaders before resolving .dll dependencies. This means you can't use custom structs as shader inputs without modifying VL.StandardLibs.

However, by using C# and the ProcessNode attribute, we can bypass this to manage our own shader nodes. As a bonus, this approach lets us compile shaders dynamically at runtime from chunks (e.g., mixins).

This example shows how to set up a minimal pipeline to get access to dynamic shader compilation.

### Setup

The first thing we need to do is make sure that the `Stride Tools Visual Studio Extension` (.vsix) works. For that, we need to install the following package:

```xml
<ItemGroup>
  <PackageReference
    Include="Stride.Core.Assets.CompilerApp"
    Version="$(StrideVersion)"
    IncludeAssets="build;buildTransitive"
    />
</ItemGroup>

```

And create an `.sdpkg` file:

```yml
!Package
SerializedVersion: {Assets: 3.1.0.0}
Meta:
    Name: VL.DynamicShader
    Version: 1.0.0
    Authors: []
    Owners: []
    Dependencies: null
AssetFolders:
    -   Path: !dir Shaders # path where shaders are stored in the C# project
ResourceFolders:
    - !dir Resources
OutputGroupDirectories: {}
ExplicitFolders: []
Bundles: []
TemplateFolders: []
RootAssets: []

```

### Instructions

You should now be able to create a `Shaders` subfolder in your `.csproj`. If you create something like `Test.sdsl` (Add class -> new `Test.sdsl`), it should automatically generate an `.sdsl` file with some HLSL code and a corresponding `Test.sdsl.cs` C# file.

You can change the `.sdsl` code to something like this:

```hlsl
shader Test {
};

```

The next step involves a bit of "voodoo mumbling":

* Stride expects shaders to be picked up by the asset compiler.
* VL automatically bundles shaders if they are located in a `shaders` subfolder.

Knowing this, we can hack into the system. Basically, we will expose our shaders to VL so it can discover them, but we will call the compilation on them ourselves.

To achieve this, we need to add a post-build action. Assuming you have the following project structure:

```sh
src/projectFile.cs
src/Shaders # folder for shaders inside the C# project 
shaders # the folder we are going to copy shaders to
project.vl

```

We need to create a post-build task in the `.csproj`:

```xml
    <ItemGroup>
        <EffectsFiles Include="Shaders\**\*.sdsl" />
        <UpToDateCheckInput Include="@(EffectsFiles)" />
    </ItemGroup>

    <Target Name="CopyShaders" AfterTargets="Build" BeforeTargets="PrepareForRun">
        <RemoveDir Directories="..\shaders" />
        <Copy SourceFiles="@(EffectsFiles)" DestinationFolder="..\shaders\%(RecursiveDir)" />
    </Target>
```

If you did everything correctly, the `shaders` folder in the root directory should now include our `Test.sdsl`.

For the next step, we will start by adding `ProcessNode` support to our project:

```text
VL.DynamicShader -> Add Folder -> "Properties"
"Properties" -> New Item -> AssemblyInfo.cs

```

```csharp
using VL.Core.Import;

[assembly: ImportAsIs(Category = "DynamicShader", Namespace = "VL.DynamicShader")]

```

Now let's define the data type we will be transferring to our shader:

```csharp
// Structs/MyStruct.cs

namespace VL.DynamicShader.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MyStruct {

        public float Test;

        public MyStruct(float test)
        {
            Test = test;
        }
    }
}

```

Next, let's define `MyShaderBase.sdsl`:

```cs
// Shaders/MyShaderBase.sdsl

shader MyShaderBase 
{
    struct MyStruct 
    {
        float Test;
    };

    stage MyStruct MyStructInput;
};

```

After doing this, check the `Shaders/MyShaderBase.sdsl.cs` file. You will notice the line:

```cs
namespace Stride.Rendering
{
    public static partial class MyShaderBaseKeys
    {
        public static readonly ValueParameterKey<MyStruct> MyStructInput = ParameterKeys.NewValue<MyStruct>();
    }
}
```

This is something that stride generates, to support custom objects as shader inputs. Upon dll load stride will discover this declaration atomagically. Notice that `MyStruct` throws an error. This is because Stride expects structs to be in a certain namespace. To fix it, modify the `.csproj` to make our struct's namespace globally available at the project level:

```xml
<ItemGroup>
  <Using Include="VL.DynamicShader.Structs" />
</ItemGroup>

```

If you check `Shaders/MyShaderBase.sdsl.cs` again, there should be no more errors.

Now, we are going to define our Draw shader:

```text
Shaders -> Add Item -> MyDraw_DrawFX.sdsl

```

```csharp
shader MyDraw_DrawFX : VS_PS_Base, MyShaderBase
{
    override stage void VSMain()
    {
        streams.ShadingPosition = mul(streams.Position, WorldViewProjection);
    }

    override stage void PSMain() 
    {
        float myInput = MyStructInput.Test;

        streams.ColorTarget = float4(myInput, 1.0, 0.0, 1.0);
    }
};

```

It is time to add `launchSettings.json` so we can launch our project in debug mode:

```text
Properties -> Add Item -> launchSettings.json

```

```json
{
  "profiles": {
    "dev/antokhio": {
      "commandName": "Executable",
      "executablePath": "C:\\\\Program Files\\\\vvvv\\\\vvvv_gamma_7.3-win-x64\\\\vvvv.exe",
      "commandLineArgs": "",
      "workingDirectory": "$(ProjectDir)"
    }
  }
}

```

```text
Start vvvv
New patch > VL.DynamicShader
Dependencies > VL Nugets > VL.Stride
Dependencies > File > Add Existing > lib > VL.DynamicShader.dll

```

After setting up the patch, you should be able to add `MyStruct -> Create` and `MyDrawShader`.
However, if you save and reload the patch, you will notice `MyDrawShader` turns red.

To fix that, we need to define a few things:

* `DynamicShaderService`: Responsible for creating an `EffectInstance`.
* `DynamicShaderNode`: A base class/process node that will manage the effect for us and handle the service.

Let's start with `DynamicShaderService`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Stride.Engine;
using Stride.Rendering;
using Stride.Shaders.Compiler;
using System.Diagnostics;
using VL.Core;

namespace VL.DynamicShader
{
    // Interface we are going to locate the service by.
    public interface IDynamicShaderService
    {
        /// <summary>
        /// Initiates a shader instance by name. The compiler will look for it in the VFS.
        /// </summary>
        EffectInstance InitiateEffect(string shaderName);
    }

    public class DynamicShaderService : IDynamicShaderService
    {
        // Segment of the path where our generated shader cache will be stored.
        const string SHADER_CACHE_PATH = "DynamicShaderCache";

        // Stride effect system.
        private readonly EffectSystem _effectSystem;

        // Path to the shader cache.
        private readonly string _cachePath;

        public DynamicShaderService(AppHost appHost)
        {
            // Get the game instance from the AppHost services. 
            var game = appHost.Services.GetService<Game>();
            if (game is null)
                throw new InvalidOperationException(
                    "ShaderService requires a Game instance to be registered in the AppHost services."
                );

            // Get the effect system from the Game services.
            var effectSystem = game.Services.GetService<EffectSystem>();
            if (effectSystem is null)
                throw new InvalidOperationException(
                    "ShaderService requires an EffectSystem instance to be registered in the Game services."
                );

            // Assign effect system
            _effectSystem = effectSystem;
        }

        public EffectInstance InitiateEffect(string shaderName)
        {          
            var compilerParameters = new CompilerParameters();
            var effectBytecode = _effectSystem
                .LoadEffect(shaderName, compilerParameters)
                .WaitForResult();

            return new EffectInstance(effectBytecode);
        }
    }
}

```

Next, we can jump into how we will wrap our shader. We can use one of VL's provided classes, `CustomDrawEffect`.

We are going to create a wrapper (a `process node`) that will hold an instance of `CustomDrawEffect` and pass parameters to it.

Let's create our abstract dynamic draw shader base:

```csharp
using Stride.Core.Mathematics;
using Stride.Rendering;
using VL.Core.Import;
using VL.Stride.Rendering;

namespace VL.DynamicShader
{
    [ProcessNode]
    public abstract class DynamicDrawShader : IEffect, IDisposable
    {
        // Holds the name of the shader we are going to wrap
        protected abstract string ShaderName { get; }

        // Instance of shader service
        private readonly IDynamicShaderService _shaderService;
        private readonly RenderContext _renderContext;

        // Used to pass world to _effect
        private IVLPin<Matrix> _world;

        // Instance of the effect
        private CustomDrawEffect _effect;

        public DynamicDrawShader(NodeContext nodeContext)
        {
            var appHost = nodeContext.AppHost;

            // This is called a lazy initializable service, e.g., it will 
            // be registered on the first call here
            _shaderService = appHost.Services.GetOrAddService<IDynamicShaderService>((_) => new DynamicShaderService(appHost));

            _renderContext = RenderContext.GetShared(
               appHost.Services.GetRequiredService<Game>().Services
           );

            Initialize();
        }
    }

    private void Initialize() {}
}   

```

* `IEffect`: Interface used to expose us as a shader instance.
* `IVLPin<Matrix>`: A world pin on the shader.
* `CustomDrawEffect`: A VL shader node container.

Let's add our business logic now:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;
using System.Diagnostics;
using VL.Core;
using VL.Core.Import;
using VL.Stride.Rendering;

namespace VL.DynamicShader
{
    [ProcessNode]
    public abstract class DynamicDrawShader : IEffect
    {
        // ...

        // Initialize creates a new shader instance
        private void Initialize()
        {
            try
            {
                // Initiate the shader effect instance using the shader service.
                var effectInstance = _shaderService.InitiateEffect(ShaderName);

                // Initialize the effect for the graphics device
                effectInstance.UpdateEffect(_renderContext.GraphicsDevice);

                // Reuse VL.Stride's own DrawFX path: it handles PerFrame (Time),
                // PerView (View/Projection), PerDraw (World/WorldViewProjection,
                // including the parent transformation) and Texturing for us.
                _effect = new CustomDrawEffect(effectInstance, _renderContext.GraphicsDevice)
                {
                    WorldIn = _world,
                    ParameterSetter = (parameters, _, _) => ApplyParameters(parameters),
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initiate shader '{ShaderName}': {ex.Message}");
            }
        }

        // This will be called by the shader on every draw
        public abstract void ApplyParameters(ParameterCollection parameters);

        // Satisfies the IEffect constraint
        [Fragment(IsHidden = true)]
        public EffectInstance SetParameters(RenderView renderView, RenderDrawContext renderDrawContext)
        {
            return _effect?.SetParameters(renderView, renderDrawContext);
        }

        // Releases the effect when disposed
        public void Dispose()
        {
            _effect?.Dispose();
        }        
    }
}

```

One more thing we have to do is create a small utility wrapper for a `World` pin on the shader. We will add it directly to our base class and assign a value to `_world`:

```csharp
namespace VL.DynamicShader
{
    [ProcessNode]
    public abstract class DynamicDrawShader : IEffect
    {
        // ...

        private IVLPin<Matrix> _world = new MatrixPin();

        // ...

        private sealed class MatrixPin : IVLPin<Matrix>
        {
            public Matrix Value { get; set; } = Matrix.Identity;

            object IVLPin.Value
            {
                get => Value;
                set => Value = (Matrix)value;
            }
        }

        public void SetWorld(Matrix world)
        {
            _world.Value = world;
        }
    }
}

```

Finally, we are going to build `MyDynamicDrawShader`:

```csharp
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

        // Place where we are going to store a struct
        private MyStruct _myStruct = new MyStruct(1.0f);

        // Sets our input from the upstream patch.
        public void SetMyStructValue(MyStruct myStruct)
        {
            _myStruct = myStruct;
        }

        // Constructor takes NodeContext and passes it to the base class.
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

```

And that's about it, voilà, your shader effect:
![whoala](/vl-stride-csharp-shader/assets/whoala.png)