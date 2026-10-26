using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>独占媒体会话状态与终止路径；旧代次只允许完成释放，不能重新发布业务状态。</summary>
    internal sealed class StreamSessionController : IDisposable
    {
        private sealed class Attempt
        {
            internal long Epoch;
            internal string Url;
            internal MediaOptions Configuration;
            internal IMediaPeer Peer;
            internal WhipSessionLease Remote;
            internal Action Unsubscribe;
            internal double StartedAt;
            internal double StoppedAt;
            internal bool DrainReported;
        }

        private readonly SessionService session;
        private readonly StreamSourceSlot sources;
        private readonly Func<RenderTexture> getOutput;
        private readonly Func<IMediaPeer> createPeer;
        private readonly IWhipExchange exchange;
        private MediaOptions options;
        private readonly Func<double> clock;
        private readonly Action<SdkResult> report;
        private readonly List<Attempt> retiring = new List<Attempt>();
        private Attempt active;
        private string desiredUrl;
        private long desiredEpoch;
        private int retries;
        private double retryAt;
        private bool disposed;
        private bool exchangeClosed;
        private bool failed;
        private bool controlStartPending;
        private bool beginning;
        private readonly bool enabled;
        private long controlRevision;
        internal StreamingState State { get; private set; }
        internal StreamingStatistics Statistics { get; private set; }
        internal bool IsReleased => disposed && active == null && retiring.Count == 0;
        internal bool CanReconfigureOutput => !disposed && active == null && retiring.Count == 0 && desiredUrl == null;

        internal void UpdateOptions(MediaOptions value)
        {
            options = value;
            if (State != StreamingState.Recovering) return;
            if (retries > value.RetryDelaysMilliseconds.Length)
            {
                long revision = ++controlRevision;
                desiredUrl = null; failed = true; controlStartPending = false;
                RetireActive();
                if (!disposed && controlRevision == revision) SetState(StreamingState.Failed);
            }
            else if (retries > 0) retryAt = clock() + value.RetryDelaysMilliseconds[retries - 1] / 1000.0;
        }
        internal event Action Starting;
        internal event Action<StreamingState> Changed;
        internal event Action<StreamingStatistics> StatisticsUpdated;

        internal StreamSessionController(SessionService session, StreamSourceSlot sources, Func<RenderTexture> getOutput,
            Func<IMediaPeer> createPeer, IWhipExchange exchange, MediaOptions options, Func<double> clock, Action<SdkResult> report, bool enabled)
        {
            this.session = session; this.sources = sources; this.getOutput = getOutput;
            this.createPeer = createPeer; this.exchange = exchange; this.options = options;
            this.clock = clock; this.report = report; this.enabled = enabled;
            session.LiveReceived += OnLive;
            session.SessionChanged += OnSession;
        }

        private void OnLive(long epoch, string url, bool start)
        {
            if (disposed || !session.Current.IsJoined || epoch != session.Current.Epoch) return;
            if (!start) { Stop(); return; }
            if (!enabled)
            { report(SdkResult.Fail(SdkErrorCode.Unavailable, "客户端未启用推流，已忽略开流通知")); return; }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            { report(SdkResult.Fail(SdkErrorCode.InvalidArgument, "推流地址必须为HTTP或HTTPS地址")); return; }
            if (desiredUrl == url && desiredEpoch == epoch && active != null) return;
            long revision = ++controlRevision;
            Stop(false);
            // 状态/清源回调可能重入新的控制命令；较旧的调用栈不得覆盖后来的意图。
            if (disposed || controlRevision != revision || session.Current.Epoch != epoch || !session.Current.IsJoined) return;
            desiredUrl = url; desiredEpoch = epoch; retries = 0; retryAt = clock(); failed = false;
            controlStartPending = true;
            Tick();
        }

        private void OnSession(SessionSnapshot snapshot)
        {
            if (snapshot.State == SessionState.Ended || snapshot.State == SessionState.Failed ||
                (desiredUrl != null && snapshot.Epoch != desiredEpoch)) Stop();
        }

        private bool IsCurrent(Attempt attempt) => !disposed && ReferenceEquals(active, attempt) && attempt.Epoch == session.Current.Epoch;

        private bool IsCurrentControl(long revision) => !disposed && enabled && controlRevision == revision &&
            desiredUrl != null && session.Current.IsJoined && session.Current.Epoch == desiredEpoch;

        private void Begin()
        {
            long revision = controlRevision;
            if (beginning || !IsCurrentControl(revision)) return;
            beginning = true;
            Attempt attempt = null;
            try
            {
                sources.SetSessionActive(true);
                SetState(StreamingState.Starting);
                if (!IsCurrentControl(revision)) return;
                if (controlStartPending && !PrepareSource(revision)) return;
                attempt = new Attempt { Epoch = desiredEpoch, Url = desiredUrl, StartedAt = clock(), Configuration = options };
                attempt.Peer = createPeer() ?? throw new InvalidOperationException("媒体会话工厂未返回实例");
                attempt.Remote = new WhipSessionLease(exchange, options, clock, report);
                active = attempt;
                Subscribe(attempt);
                var output = getOutput();
                if (!IsCurrent(attempt) || !IsCurrentControl(revision)) return;
                if (output == null) throw new InvalidOperationException("无法创建推流输出纹理");
                attempt.Peer.Start(output, attempt.Epoch);
            }
            catch (Exception error)
            {
                if (attempt?.Peer == null)
                {
                    if (!IsCurrentControl(revision)) return;
                    desiredUrl = null; failed = true; controlStartPending = false;
                    sources.EndSession();
                    report(SdkResult.Fail(SdkErrorCode.MediaFailed, "准备媒体会话失败：" + error.Message));
                    if (!disposed && controlRevision == revision) SetState(StreamingState.Failed);
                }
                else Fail(attempt, SdkResult.Fail(SdkErrorCode.MediaFailed, "启动媒体会话失败：" + error.Message), false);
            }
            finally { beginning = false; }
        }

        private bool PrepareSource(long revision)
        {
            controlStartPending = false;
            // 允许业务回调和导演自动相机先接源；准备期间的Tick不能递归创建第二个会话。
            Notify(Starting);
            if (!IsCurrentControl(revision)) return false;
            bool ready = options.AllowBlackFrameStart || sources.HasAvailableSource;
            if (!IsCurrentControl(revision)) return false;
            if (ready) return true;
            // 拒绝即撤销本次意图：后续补源/配置更新不能悄悄执行旧通知。
            Stop();
            report(SdkResult.Fail(SdkErrorCode.Unavailable, "首次开流画面源未就绪，已拒绝本次通知；接源后需新的开流通知"));
            return false;
        }

        private void Subscribe(Attempt attempt)
        {
            Action<string> offer = value => OnOffer(attempt, value);
            Action connected = () => { if (IsCurrent(attempt)) SetState(StreamingState.Streaming); };
            Action<SdkResult, bool> failure = (error, retryable) => Fail(attempt, error, retryable);
            Action<StreamingStatistics> stats = value =>
            {
                if (!IsCurrent(attempt) || State != StreamingState.Streaming) return;
                Statistics = new StreamingStatistics(attempt.Epoch, value.Timestamp, value.VideoBitrateKbps,
                    value.AudioBitrateKbps, value.RttMilliseconds, value.PacketsLost);
                Notify(StatisticsUpdated, Statistics);
            };
            attempt.Peer.LocalOffer += offer;
            attempt.Peer.Connected += connected;
            attempt.Peer.Failed += failure;
            attempt.Peer.Statistics += stats;
            attempt.Unsubscribe = () =>
            {
                attempt.Peer.LocalOffer -= offer; attempt.Peer.Connected -= connected;
                attempt.Peer.Failed -= failure; attempt.Peer.Statistics -= stats;
            };
        }

        private void OnOffer(Attempt attempt, string offer)
        {
            if (!IsCurrent(attempt)) return;
            try
            {
                attempt.Remote.Start(attempt.Url, offer, reply =>
                {
                    if (!IsCurrent(attempt)) return;
                    if (!reply.Result.Success) { Fail(attempt, reply.Result, reply.Retryable); return; }
                    if (string.IsNullOrWhiteSpace(reply.Answer))
                    { Fail(attempt, SdkResult.Fail(SdkErrorCode.InvalidResponse, "WHIP未返回SDP answer"), false); return; }
                    try { attempt.Peer.SetAnswer(reply.Answer); }
                    catch (Exception error) { Fail(attempt, SdkResult.Fail(SdkErrorCode.MediaFailed, "设置SDP answer失败：" + error.Message), false); }
                });
            }
            catch (Exception error) { Fail(attempt, SdkResult.Fail(SdkErrorCode.TransportFailed, "发送WHIP失败：" + error.Message), true); }
        }

        private void Fail(Attempt attempt, SdkResult error, bool retryable)
        {
            if (!IsCurrent(attempt)) return;
            bool recover = retryable && desiredUrl != null && retries < options.RetryDelaysMilliseconds.Length;
            RetireActive(!recover);
            if (recover)
            {
                retryAt = clock() + options.RetryDelaysMilliseconds[retries++] / 1000.0;
                SetState(StreamingState.Recovering);
            }
            else
            {
                desiredUrl = null; failed = true;
                SetState(StreamingState.Failed);
            }
            report(error);
        }

        private void RetireActive(bool endSource = true)
        {
            Statistics = default;
            var previous = active;
            active = null;
            if (previous != null)
            {
                previous.StoppedAt = clock();
                retiring.Add(previous);
                previous.Unsubscribe?.Invoke(); previous.Unsubscribe = null;
                previous.Remote.Close();
                previous.Peer.Dispose();
            }
            // native尝试失败不撤销当前控制授权；恢复期间保留同一源，终止才释放自动源。
            if (endSource) sources.EndSession();
        }

        private void Stop(bool invalidateControl = true)
        {
            if (invalidateControl) ++controlRevision;
            desiredUrl = null; retryAt = 0; retries = 0; failed = false; controlStartPending = false;
            RetireActive();
            CollectRetired();
            SetState(retiring.Count == 0 ? StreamingState.Idle : StreamingState.Stopping);
        }

        internal void Tick()
        {
            active?.Peer.Tick();
            if (active != null && State == StreamingState.Starting && clock() - active.StartedAt >= active.Configuration.HandshakeTimeoutSeconds)
                Fail(active, SdkResult.Fail(SdkErrorCode.Timeout, "媒体协商超过总超时"), true);
            CollectRetired();
            if (active != null) return;
            if (disposed)
            {
                SetState(retiring.Count == 0 ? StreamingState.Idle : StreamingState.Stopping);
                if (IsReleased && !exchangeClosed) { exchangeClosed = true; exchange.Dispose(); }
                return;
            }
            if (retiring.Count != 0) return;
            if (!beginning && desiredUrl != null && clock() >= retryAt) Begin();
            else if (desiredUrl == null) SetState(failed ? StreamingState.Failed : StreamingState.Idle);
        }

        private void CollectRetired()
        {
            for (int index = retiring.Count - 1; index >= 0; index--)
            {
                var attempt = retiring[index];
                attempt.Peer.Tick();
                attempt.Remote.Tick();
                if (attempt.Peer.IsReleased && attempt.Remote.IsReleased) { retiring.RemoveAt(index); continue; }
                if (!attempt.Peer.IsReleased && !attempt.DrainReported && clock() - attempt.StoppedAt >= attempt.Configuration.AudioDrainWarningSeconds)
                {
                    attempt.DrainReported = true;
                    report(SdkResult.Fail(SdkErrorCode.AudioDrainStalled, "音频在途回调尚未退出，继续等待，不强制释放native资源"));
                }
            }
        }

        private void SetState(StreamingState value)
        {
            if (State == value) return;
            State = value;
            session.Log.Write(SdkLogLevel.Info, "Streaming", "推流状态：" + value, sessionEpoch: session.Current.Epoch);
            Notify(Changed, value);
        }

        private void Notify(Action callbacks)
        {
            if (callbacks == null) return;
            foreach (Action callback in callbacks.GetInvocationList())
            { try { callback(); } catch (Exception error) { session.Log.Exception("Streaming", error); } }
        }

        private void Notify<T>(Action<T> callbacks, T value)
        {
            if (callbacks == null) return;
            foreach (Action<T> callback in callbacks.GetInvocationList())
            { try { callback(value); } catch (Exception error) { session.Log.Exception("Streaming", error); } }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.LiveReceived -= OnLive; session.SessionChanged -= OnSession;
            Stop(); Tick();
            Starting = null; Changed = null; StatisticsUpdated = null;
        }
    }
}
