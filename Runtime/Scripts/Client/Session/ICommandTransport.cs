using System;

namespace MyVerseXRSDK
{
    internal enum TransportState { Disconnected, Connecting, Connected, WaitingRetry, Failed, Kicked }

    internal interface ICommandTransport : IDisposable
    {
        bool IsConnected { get; }
        event Action<TransportState> Changed;
        IDisposable Subscribe(string messageType, Action<int, byte[]> callback);
        IDisposable Send(string api, byte[] payload, Action<int, byte[]> callback = null);
        void Connect(string address);
        void Disconnect();
        void Tick();
    }

    internal interface INetworkSocket : IDisposable
    {
        event Action Opened;
        event Action<byte[]> Received;
        event Action<string> Closed;
        void Connect(string address);
        void Send(byte[] data);
        void Tick();
    }
}
