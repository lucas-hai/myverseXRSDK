using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>自助积分验证；成功结果保留至客户端释放，非幂等请求失败后不自动重试。</summary>
    public sealed class BillingService : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly Func<string> endpoint;
        private readonly IHttpRequestRunner http;
        private readonly CommandScope scope;
        private bool approved;
        private OperationHandle pending;
        private bool disposed;

        internal BillingService(SdkRuntimeContext context, Func<string> endpoint, IHttpRequestRunner http)
        {
            this.context = context;
            this.endpoint = endpoint;
            this.http = http;
            scope = new CommandScope(context, "Billing");
        }

        /// <summary>显式请求扣费；同次初始化已成功时返回缓存，完成回调始终延后派发。</summary>
        public OperationHandle VerifyAccess(Action<OperationResult> completed)
        {
            var operation = scope.Begin(null, completed);
            if (!context.IsRunning) return Reject(operation, SdkErrorCode.InvalidState, "客户端尚未启动");
            if (approved)
            { operation.TryComplete(SdkResult.Ok, true); return operation; }
            if (pending != null && !pending.IsCompleted) return Reject(operation, SdkErrorCode.Busy, "已有积分请求进行中");
            string baseUrl = endpoint();
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                return Reject(operation, SdkErrorCode.Unavailable, "未配置有效的账务端点");
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                gameType = 1, roomId = string.Empty, gamePackName = Application.identifier,
                devIds = new[] { context.Options.DeviceId }, feeeType = 1, gameDuration = 0
            }));
            pending = operation;
            operation.Finished += ClearPending;
            operation.MarkSent();
            try
            {
                var lease = http.Begin("POST", baseUrl.TrimEnd('/') + "/vrcontrol/frontend/v1/customer/intergral/deduction", body,
                    reply => Complete(operation, reply));
                CommandScope.AttachLease(operation, lease);
            }
            catch (Exception error)
            { operation.TryComplete(SdkResult.Fail(SdkErrorCode.TransportFailed, error.Message), false, true); }
            return operation;
        }

        private void Complete(OperationHandle operation, HttpReply reply)
        {
            if (disposed || operation.IsCompleted) return;
            if (!reply.Result.Success) { operation.TryComplete(reply.Result, false, true); return; }
            try
            {
                var response = JObject.Parse(reply.Body);
                var codeToken = response["code"];
                if (codeToken == null || codeToken.Type != JTokenType.Integer ||
                    !int.TryParse(codeToken.ToString(Formatting.None), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int code))
                { operation.TryComplete(SdkResult.Fail(SdkErrorCode.InvalidResponse, "账务响应code必须为32位整数"), false, true); return; }
                if (code != 0) { operation.TryComplete(SdkResult.Fail(SdkErrorCode.RemoteRejected, "账务服务拒绝验证")); return; }
                approved = true;
                operation.TryComplete(SdkResult.Ok);
            }
            catch (JsonException error)
            { operation.TryComplete(SdkResult.Fail(SdkErrorCode.InvalidResponse, error.Message), false, true); }
        }

        private void ClearPending(OperationHandle operation)
        {
            operation.Finished -= ClearPending;
            if (ReferenceEquals(pending, operation)) pending = null;
        }

        private static OperationHandle Reject(OperationHandle operation, SdkErrorCode code, string message)
        { operation.TryComplete(SdkResult.Fail(code, message)); return operation; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scope.Dispose();
            http.Dispose();
            approved = false;
        }
    }
}
