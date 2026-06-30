# vl-stride-csharp-shader (WIP)

Shows how to run an manage your custom shader from C# using `VL.Stride.Runtime`.

The problem: 

To create shaders e.g. nodes, VL usese some sort of mechanics that is bound to vl patch. E.g. shaders are discovered, compiled and nodes assembled before dependencies `dll`s are resolved.
As a result if you want a custom struct as an shader input, it's impossible unless you customize `VL.StandartLibs`, however using c# and `ProcessNode` attribute we can create and manage our own shader node, moreover this method also allows to compile shaders at runtime dynamically from chunks e.g. mixins.

This example shows how to setup minimal pipeline to get access to dynamic shader compilation.

### Setup

First thing we are going to need is to make sure that `Stride Tools Visual Studio Extension` - `visix` works. For that we need to install:

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
    -   Path: !dir Shaders # path where shaders are stored in c# project
ResourceFolders:
    - !dir Resources
OutputGroupDirectories: {}
ExplicitFolders: []
Bundles: []
TemplateFolders: []
RootAssets: []
```

### Instruction

So here you should be able to create `Shaders` subfolder in you `csproj` and if you create something like `Test.sdls` (Add class new `Test.sdsl`) you should see it creates an sdsl file with some c# code and a `Test.sdsl.cs` c# file.

You can change sdsl code to something like this:
```hlsl
shader Test {
};
```

The next, step is something i would call a `voodoo mumbling`:

* Stride expects shaders to be picked up by asset compiler.
* VL already bundles shaders if they are in the `shaders` subfolder.

Knowing this we can kinda `hack` in to this system, basicaly we will give vl our shaders, so it can discover them, but we will call a compile on them our self. 

For that we need to add a post build action, so if you have project structure:

```sh
src/projectFile.cs
src/Shaders // folder for shaders ins 
shaders // the folder we are going to copy shaders to
project.vl

```

So we need to create an post build task:

```xml
<!-- Copy effects to ../shader on build -->
<Target Name="CopyShaders" AfterTargets="Build">
    <ItemGroup>
        <EffectsFiles Include="Shaders\**\*.sdsl" />
    </ItemGroup>
    <Copy SourceFiles="@(EffectsFiles)" DestinationFolder="..\shaders\%(RecursiveDir)" />
</Target>
```

I you did everything correct the `shaders` folder in root should include our `Test.sdsl`.

For next step we are going start by adding `ProcessNode` support to our project:
```
VL.DynamicShader -> Add Folder -> "Properties"
"Properties" -> New Item -> AssemblyInfo.cs
```

```
using VL.Core.Import;

[assembly: ImportAsIs(Category = "DynamicShader", Namespace = "VL.DynamicShader")]
```

Now let's define our data type that we will be transfering to our shader:

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

Now let's define `MyShaderBase.sdsl`

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

After that we need to check the `Shaders/MyShaderBase.sdsl.cs` file. Notice line that mentions `MyStruct` is errored. That because stride expects structs to be in certain namespace, to fix that we need to modify `csproj` to make our struct namespace globally avalible on project level:

```xml
<ItemGroup>
  <Using Include="VL.DynamicShader.Structs" />
</ItemGroup>
```

Now if we check `Shaders/MyShaderBase.sdsl.cs` again we should see it has no erros anymore.

Next step we are going to define our Draw shader. So:
```
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

So it is time to add `launchSettings.json` so we can start our project in debug.
```
Properties -> Add Item -> launchSettings.json
```

```
{
  "profiles": {
    "dev/antokhio": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\vvvv\\vvvv_gamma_7.3-win-x64\\vvvv.exe",
      "commandLineArgs": "",
      "workingDirectory": "$(ProjectDir)"
    },   
  }
}
```

```
Start vvvv
New patch > VL.DynamicShader
Dependenices > VL Nugets > VL.Stride
Dependencies > File > Add Existing > lib > VL.DynamicShader.dll
```

After you setup a patch, you should be able to add `MyStruct -> Create` and `MyDrawShader`
However if you save and reload patch you will notice `MyDrawShader` will become red.

To fix that we are going to need to define a few stuff:

* `DynamicShaderService` - will be responsible for creating an `EffectInstance` 
* `DynamicShaderNode` - an base `class / process node` that would manage effect for us and handle service.

So let's start with a `DynamicShaderService`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Stride.Engine;
using Stride.Rendering;
using Stride.Shaders.Compiler;
using System.Diagnostics;
using VL.Core;
using VirtualFileSystem = Stride.Core.IO.VirtualFileSystem;

namespace VL.DynamicShader
{
    // Interface we are going to locate service by.
    public interface IDynamicShaderService
    {
        /// <summary>
        /// Initiates a shader instance by name. The compiler will look for it in the VFS.
        /// </summary>
        EffectInstance InitiateEffect(string shaderName);
    }

    public class DynamicShaderService : IDynamicShaderService
    {
        // Segment of path where our generated shader cache will be stored.
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

            // Create a shader cache directory
            _cachePath = Path.Combine(Path.GetTempPath(), SHADER_CACHE_PATH);

            // Mount the shader cache directory if not mounted already.
            if (!VirtualFileSystem.DirectoryExists("/shaders/dynamic"))
            {
                Directory.CreateDirectory(_cachePath);
                VirtualFileSystem.MountFileSystem("/shaders/dynamic", _cachePath);
            }
            else
            {
                Debug.WriteLine("Virtual FileSystem already mounted at /shaders/dynamic");
            }
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

Ok, next we can jump on to how we are going to wrap our shader, for that we can use one of VL already provided classes [CustomDrawEffect](https://github.com/vvvv/VL.StandardLibs/blob/d0f888370873c5e132b63bb0172638a27e73784f/VL.Stride.Runtime/src/Rendering/Effects/CustomDrawEffect.cs)

So what we are going to do, is create a `wrapper` - `process node` that would hold instance of `CustomDrawEffect` and pass through parameters on it. 

So let's create our abstract dynamic draw shader base:

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
        // Holds name of shader we are going to wrap
        protected abstract string ShaderName { get; }

        // Instance of shader service
        private readonly IDynamicShaderService _shaderService;
        private readonly RenderContext _renderContext;

        // Used to pass world to _effect
        private IVLPin<Matrix> _world;

        // Instance of effect
        private CustomDrawEffect _effect;

        public DynamicDrawShader(NodeContext nodeContext)
        {
            var appHost = nodeContext.AppHost;

            // This called a lazy initializable service, e.g. it would 
            // be registered on first call here
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

* `IEffect` - is an interface used to discover us as an shader instance [source](https://github.com/vvvv/VL.StandardLibs/blob/d0f888370873c5e132b63bb0172638a27e73784f/VL.Stride.Runtime/src/Rendering/Effects/IEffect.cs)
* `IVLPin<Matrix>` - is an world pin on shader.
* `CustomDrawEffect` - is an VL shader node container.


Let's now fire of our buiseness logic now:

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

        // Initialize create new shader instance
        private void Initialize()
        {
            try
            {
                // Initiate the shader effect instance using the shader service.
                var effectInstance = _shaderService.InitiateEffect(ShaderName);

                // Initialize effect for graphics device
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

        // This will be called by shader every draw
        public abstract void ApplyParameters(ParameterCollection parameters);

        // Satisfies IEffect constraint
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

Allright one more thing we have to do is small utility wrapper, that would be a `World` pin on a shader, we will add it directly to our base calss and change `_world` to have an value:

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

Next we are going to do my `MyDynamicDrawShader` so:

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
```

So thats about it, whoala your shader effect: 

![image](vl-stride-csharp-shader/assets/whoala.png)