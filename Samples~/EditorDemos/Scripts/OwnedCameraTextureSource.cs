using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>自定义IStreamSource示例：独立相机生成普通RT，SDK接管适配器后由其Dispose释放自有RT；相机本体借用。</summary>
    public sealed class OwnedCameraTextureSource : IStreamSource
    {
        private readonly Camera producer;
        private readonly RenderTextureStreamSource textureSource;
        private RenderTexture originalTarget;
        private bool originalEnabled, attached, disposed;
        public RenderTexture Texture { get; private set; }
        public RenderTexture Output { get; private set; }
        public int Width => Texture != null ? Texture.width : 0;
        public int Height => Texture != null ? Texture.height : 0;
        public string DisplayName => "Editor独立相机普通RT";
        public bool IsAvailable => !disposed && producer != null && Texture != null && (!attached || producer.targetTexture == Texture);
        public event Action OnAttached;
        public event Action OnDetached;

        public OwnedCameraTextureSource(Camera producer)
        {
            this.producer = producer != null ? producer : throw new ArgumentNullException(nameof(producer));
            Texture = new RenderTexture(640, 360, 24) { name = "EditorDemo.OwnedRT" };
            if (!Texture.Create()) { Destroy(Texture); Texture = null; throw new InvalidOperationException("示例RT创建失败"); }
            textureSource = new RenderTextureStreamSource(Texture);
        }
        public void Attach(RenderTexture output)
        {
            if (disposed || attached) throw new InvalidOperationException("源已释放或已接上输出");
            textureSource.Attach(output);
            originalTarget = producer.targetTexture; originalEnabled = producer.enabled;
            producer.targetTexture = Texture; producer.enabled = true;
            Output = output; attached = true;
            OnAttached?.Invoke();
        }
        public void Tick(double now, int framesPerSecond) => textureSource.Tick(now, framesPerSecond);
        public void Detach()
        {
            if (!attached) return;
            textureSource.Detach();
            if (producer != null && producer.targetTexture == Texture)
            { producer.targetTexture = originalTarget; producer.enabled = originalEnabled; }
            originalTarget = null; Output = null; attached = false;
            OnDetached?.Invoke();
        }
        public void Dispose()
        {
            if (disposed) return;
            try { Detach(); }
            finally
            {
                disposed = true; textureSource.Dispose();
                if (Texture != null) { Texture.Release(); Destroy(Texture); Texture = null; }
            }
        }
        private static void Destroy(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
