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
        protected abstract string ShaderName { get; }

        private readonly IDynamicShaderService _shaderService;
        private readonly RenderContext _renderContext;

        private IVLPin<Matrix> _world = new MatrixPin();
        private CustomDrawEffect _effect;

        // Constructor takes NodeContext and locates necessary services.
        public DynamicDrawShader(NodeContext nodeContext)
        {
            var appHost = nodeContext.AppHost;

            _shaderService = appHost.Services.GetOrAddService<IDynamicShaderService>((_) => new DynamicShaderService(appHost));

            _renderContext = RenderContext.GetShared(
               appHost.Services.GetRequiredService<Game>().Services
           );

            Initialize();
        }

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
        protected abstract void ApplyParameters(ParameterCollection parameters);

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