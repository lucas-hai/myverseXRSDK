using System;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 字符串 key 事件总线。设计上保留给"跨 Manager 解耦的少量信号"使用（ROOM_DISBAND / LOGIN_SUCCESS /
    /// SOCKET_RECONNECT_FAILED 等），新代码强类型通信优先用 Store / C# event。
    ///
    /// 支持 0-3 参数变体（覆盖现有所有真实使用，留 1 档余量）；4+ 参数请改 Store 或拆数据结构。
    /// </summary>
    internal static class EventSystem
    {
        private static EventModule eventModule;

        public static void Init()
        {
            eventModule = new EventModule();
        }

        /// <summary>
        /// SDK UnInit 末尾调用：清空模块内监听 + null 模块引用，让下次 Init 创建全新 EventModule。
        /// </summary>
        internal static void ResetForUnInit()
        {
            if (eventModule != null)
            {
                eventModule.Clear();
                eventModule = null;
            }
        }

        #region 添加监听

        public static void AddEventListener(string eventName, Action action)
        {
            eventModule.AddEventListener(eventName, action);
        }

        public static void AddEventListener<T>(string eventName, Action<T> action)
        {
            eventModule.AddEventListener<Action<T>>(eventName, action);
        }

        public static void AddEventListener<T0, T1>(string eventName, Action<T0, T1> action)
        {
            eventModule.AddEventListener(eventName, action);
        }

        public static void AddEventListener<T0, T1, T2>(string eventName, Action<T0, T1, T2> action)
        {
            eventModule.AddEventListener(eventName, action);
        }

        #endregion

        #region 触发事件

        public static void EventTrigger(string eventName)
        {
            eventModule.EventTrigger(eventName);
        }

        public static void EventTrigger<T>(string eventName, T arg)
        {
            eventModule.EventTrigger<T>(eventName, arg);
        }

        public static void EventTrigger<T0, T1>(string eventName, T0 arg0, T1 arg1)
        {
            eventModule.EventTrigger(eventName, arg0, arg1);
        }

        public static void EventTrigger<T0, T1, T2>(string eventName, T0 arg0, T1 arg1, T2 arg2)
        {
            eventModule.EventTrigger<T0, T1, T2>(eventName, arg0, arg1, arg2);
        }

        #endregion

        #region 取消监听

        public static void RemoveEventListener(string eventName, Action action)
        {
            eventModule.RemoveEventListener(eventName, action);
        }

        public static void RemoveEventListener<T>(string eventName, Action<T> action)
        {
            eventModule.RemoveEventListener(eventName, action);
        }

        public static void RemoveEventListener<T0, T1>(string eventName, Action<T0, T1> action)
        {
            eventModule.RemoveEventListener(eventName, action);
        }

        public static void RemoveEventListener<T0, T1, T2>(string eventName, Action<T0, T1, T2> action)
        {
            eventModule.RemoveEventListener(eventName, action);
        }

        #endregion

        #region 移除事件

        public static void RemoveEvent(string eventName)
        {
            eventModule.RemoveEvent(eventName);
        }

        /// <summary>清空事件中心</summary>
        public static void Clear()
        {
            eventModule.Clear();
        }

        #endregion

        #region 类型事件（以 typeof(T).Name 作为事件名）

        public static void AddTypeEventListener<T>(Action<T> action)
        {
            AddEventListener<T>(typeof(T).Name, action);
        }

        public static void RemoveTypeEvent<T>()
        {
            RemoveEvent(typeof(T).Name);
        }

        public static void RemoveTypeEventListener<T>(Action<T> action)
        {
            eventModule.RemoveEventListener(typeof(T).Name, action);
        }

        public static void TypeEventTrigger<T>(T arg)
        {
            EventTrigger(typeof(T).Name, arg);
        }

        #endregion
    }
}
