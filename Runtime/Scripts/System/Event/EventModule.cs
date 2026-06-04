using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    internal class EventModule
    {
        private static ObjectPoolModule objectPoolModule = new ObjectPoolModule();

        private Dictionary<string, IEventInfo> eventInfoDic = new Dictionary<string, IEventInfo>();

        #region 内部接口、内部类

        private interface IEventInfo { void Destory(); }

        /// <summary>无参事件信息</summary>
        private class EventInfo : IEventInfo
        {
            public Action action;
            public void Init(Action action) { this.action = action; }
            public void Destory()
            {
                action = null;
                objectPoolModule.PushObject(this);
            }
        }

        /// <summary>多参 Action 事件信息（通用泛型容器，无需按参数数量重载）</summary>
        private class MultipleParameterEventInfo<TAction> : IEventInfo where TAction : MulticastDelegate
        {
            public TAction action;
            public void Init(TAction action) { this.action = action; }
            public void Destory()
            {
                action = null;
                objectPoolModule.PushObject(this);
            }
        };
        #endregion

        #region 添加事件监听

        public void AddEventListener(string eventName, Action action)
        {
            if (eventInfoDic.ContainsKey(eventName))
            {
                (eventInfoDic[eventName] as EventInfo).action += action;
            }
            else
            {
                EventInfo eventInfo = objectPoolModule.GetObject<EventInfo>();
                if (eventInfo == null) eventInfo = new EventInfo();
                eventInfo.Init(action);
                eventInfoDic.Add(eventName, eventInfo);
            }
        }

        /// <summary>多参监听（任意参数数量都走这条；MulticastDelegate 约束保证类型一致）</summary>
        public void AddEventListener<TAction>(string eventName, TAction action) where TAction : MulticastDelegate
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
            {
                MultipleParameterEventInfo<TAction> info = (MultipleParameterEventInfo<TAction>)eventInfo;
                info.action = (TAction)Delegate.Combine(info.action, action);
            }
            else AddMultipleParameterEventInfo(eventName, action);
        }

        private void AddMultipleParameterEventInfo<TAction>(string eventName, TAction action) where TAction : MulticastDelegate
        {
            MultipleParameterEventInfo<TAction> newEventInfo = objectPoolModule.GetObject<MultipleParameterEventInfo<TAction>>();
            if (newEventInfo == null) newEventInfo = new MultipleParameterEventInfo<TAction>();
            newEventInfo.Init(action);
            eventInfoDic.Add(eventName, newEventInfo);
        }
        #endregion

        #region 触发事件（按参数数量手写重载——避免 params 数组 GC / 装箱）

        public void EventTrigger(string eventName)
        {
            if (eventInfoDic.ContainsKey(eventName))
            {
                ((EventInfo)eventInfoDic[eventName]).action?.Invoke();
            }
        }

        public void EventTrigger<T>(string eventName, T arg)
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
                ((MultipleParameterEventInfo<Action<T>>)eventInfo).action?.Invoke(arg);
        }

        public void EventTrigger<T0, T1>(string eventName, T0 arg0, T1 arg1)
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
                ((MultipleParameterEventInfo<Action<T0, T1>>)eventInfo).action?.Invoke(arg0, arg1);
        }

        public void EventTrigger<T0, T1, T2>(string eventName, T0 arg0, T1 arg1, T2 arg2)
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
                ((MultipleParameterEventInfo<Action<T0, T1, T2>>)eventInfo).action?.Invoke(arg0, arg1, arg2);
        }

        #endregion

        #region 取消监听

        public void RemoveEventListener(string eventName, Action action)
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
            {
                ((EventInfo)eventInfo).action -= action;
            }
        }

        public void RemoveEventListener<TAction>(string eventName, TAction action) where TAction : MulticastDelegate
        {
            if (eventInfoDic.TryGetValue(eventName, out IEventInfo eventInfo))
            {
                MultipleParameterEventInfo<TAction> info = (MultipleParameterEventInfo<TAction>)eventInfo;
                info.action = (TAction)Delegate.Remove(info.action, action);
            }
        }
        #endregion

        #region 移除事件

        public void RemoveEvent(string eventName)
        {
            if (eventInfoDic.Remove(eventName, out IEventInfo eventInfo))
            {
                eventInfo.Destory();
            }
        }

        public void Clear()
        {
            foreach (string eventName in eventInfoDic.Keys)
            {
                eventInfoDic[eventName].Destory();
            }
            eventInfoDic.Clear();
        }
        #endregion
    }
}
