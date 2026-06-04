using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 中控同步过来的状态
    /// </summary>
    internal struct RoomStatusType
    {

        public const string Normal = "1";
        public const string Play = "2";
        public const string Del = "3";
        public const string End = "4";



    }

    internal enum ObstacleType
    {
        None = 0,
        Rect = 1,
        Oval = 2,

    }

    internal enum RoomAllocationStatus
    {
        None = 0,
        Allocated = 1,
        Undistributed = 2,
    }

    /// <summary>
    /// SDK 启动模式。决定 InitMVXRSDK 是否拉起网络阶段（HTTP / WS）。
    /// </summary>
    public enum InitMode
    {
        /// <summary>正式模式：本地 HTTP(localhost:8868) 拉 WS 地址 → 连 WS → 登录。业务方默认走这条。</summary>
        Production = 0,
        /// <summary>测试模式：跳过 HTTP，外部传入 WS 地址直连+登录。用于无中控环境下验证网络模块。</summary>
        WsDirect = 1,
        /// <summary>测试模式：完全离线，只装配本地 Manager（测试推流/节点等本地能力）。</summary>
        Offline = 2,
    }

}