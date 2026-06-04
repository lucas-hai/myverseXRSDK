namespace MyVerseXRSDK
{
    /// <summary>
    /// Http 请求的回调数据
    /// </summary>
    internal class HttpCallBackArgs
    {
        /// <summary>是否有错</summary>
        public bool HasError;

        /// <summary>响应内容（成功时为返回 body，失败时为错误信息）</summary>
        public string Value;
    }
}
