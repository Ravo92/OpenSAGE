﻿using OpenSage.Core.Graphics;
using OpenSage.Diagnostics;
using OpenSage.Rendering;

namespace OpenSage.Graphics.Shaders
{
    internal sealed class ShaderResourceManager : DisposableBase
    {
        public readonly GlobalShaderResources Global;

        public readonly ParticleShaderResources Particle;
        public readonly RoadShaderResources Road;
        public readonly TerrainShaderResources Terrain;

        public ShaderResourceManager(
            GraphicsDeviceManager graphicsDeviceManager,
            ShaderSetStore shaderSetStore)
        {
            using (GameTrace.TraceDurationEvent("ShaderResourceManager()"))
            {
                Global = AddDisposable(new GlobalShaderResources(graphicsDeviceManager.GraphicsDevice));
                
                Particle = AddDisposable(new ParticleShaderResources(shaderSetStore));
                Road = AddDisposable(new RoadShaderResources(shaderSetStore));
                Terrain = AddDisposable(new TerrainShaderResources(shaderSetStore));
            }
        }
    }
}
