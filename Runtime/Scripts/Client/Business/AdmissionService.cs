using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>中控游戏准入信号，不执行扣费或游戏状态切换。</summary>
    public sealed class AdmissionService : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly SessionService session;
        private bool disposed;
        public GameStartRequest? LastRequest { get; private set; }
        public event Action<GameStartRequest> StartRequested;

        internal AdmissionService(SdkRuntimeContext context, SessionService session)
        {
            this.context = context;
            this.session = session;
            session.AdmissionReceived += OnRequest;
            session.SessionChanged += OnSession;
        }

        private void OnRequest(GameStartRequest request)
        {
            if (disposed || !context.IsRunning || !session.Current.IsJoined || request.DeviceId != context.Options.DeviceId) return;
            LastRequest = request;
            var callbacks = StartRequested;
            if (callbacks == null) return;
            foreach (Action<GameStartRequest> callback in callbacks.GetInvocationList())
            {
                try { callback(request); }
                catch (Exception error) { context.Log.Exception("Admission", error); }
            }
        }

        private void OnSession(SessionSnapshot _) => LastRequest = null;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.AdmissionReceived -= OnRequest;
            session.SessionChanged -= OnSession;
            LastRequest = null;
            StartRequested = null;
        }
    }
}
