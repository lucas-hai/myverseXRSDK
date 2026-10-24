using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal interface IMediaPeer : IDisposable
    {
        event Action<string> LocalOffer;
        event Action Connected;
        event Action<SdkResult, bool> Failed;
        event Action<StreamingStatistics> Statistics;
        bool IsReleased { get; }
        void Start(RenderTexture output, long sessionEpoch);
        void SetAnswer(string answer);
        void Tick();
    }

    internal readonly struct WhipReply
    {
        internal SdkResult Result { get; }
        internal string Answer { get; }
        internal string Location { get; }
        internal bool Retryable { get; }
        internal WhipReply(SdkResult result, string answer, string location, bool retryable = false)
        { Result = result; Answer = answer; Location = location; Retryable = retryable; }
    }

    internal interface IWhipExchange : IDisposable
    {
        IDisposable Post(string url, string offer, int timeoutSeconds, Action<WhipReply> completed);
        IDisposable Delete(string location, int timeoutSeconds, Action<SdkResult> completed);
    }
}
