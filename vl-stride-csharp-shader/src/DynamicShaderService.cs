using Microsoft.Extensions.DependencyInjection;
using Stride.Engine;
using Stride.Rendering;
using Stride.Shaders.Compiler;
using VL.Core;

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
        // Stride effect system.
        private readonly EffectSystem _effectSystem;

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
            var effectBytecode = _effectSystem.LoadEffect(shaderName).WaitForResult();

            return new EffectInstance(effectBytecode);
        }
    }
}
