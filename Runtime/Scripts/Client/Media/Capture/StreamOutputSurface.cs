using System;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyVerseXRSDK
{
    internal interface IStreamOutput : IDisposable
    {
        RenderTexture Ensure();
        void Clear();
        void Reconfigure(int longSide);
    }

    /// <summary>编码器固定借用的普通2D输出；清源不会更换或释放纹理。</summary>
    internal sealed class StreamOutputSurface : IStreamOutput
    {
        private int longSide;
        private RenderTexture texture;
        internal StreamOutputSurface(int longSide) => this.longSide = longSide;

        public RenderTexture Ensure()
        {
            if (texture != null) return texture;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("当前没有图形设备，无法创建推流输出纹理");
            var size = ComputeSize(longSide);
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var candidate = new RenderTexture(size.x, size.y, 24, format, RenderTextureReadWrite.Default)
                { name = "MVXRSDK.StreamOutput", hideFlags = HideFlags.DontSave };
            if (!candidate.Create())
            {
                Destroy(candidate);
                throw new InvalidOperationException("创建推流输出纹理失败");
            }
            texture = candidate;
            Clear();
            return texture;
        }

        internal static Vector2Int ComputeSize(int value)
        {
            if (value < 146 || value > 4096) throw new ArgumentOutOfRangeException(nameof(value), "输出长边须在146–4096之间");
            return new Vector2Int(value & ~1, (int)Math.Round(value * 9.0 / 16.0) & ~1);
        }

        public void Clear()
        {
            if (texture == null) return;
            var previous = RenderTexture.active;
            try { RenderTexture.active = texture; GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previous; }
        }

        public void Reconfigure(int value)
        {
            if (longSide == value) return;
            Dispose();
            longSide = value;
        }

        public void Dispose()
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        private static void Destroy(RenderTexture value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
