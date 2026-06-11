using UnityEngine;

namespace MyVerseXRSDK
{



    /// <summary>
    /// 框架根节点
    /// </summary>
    //[DefaultExecutionOrder(-1000)]
    internal class MVXRSDKManager : MonoBehaviour
    {

        private void Awake()
        {
            RoomManager.Start();
            BusinessManager.Start();
            NetworkTransformManager.Start();
            SpaceManager.Start();
            DontDestroyOnLoad(gameObject);
        }
        private void OnApplicationQuit()
        {
            // 尽力而为：SendAsync 异步排队，进程退出瞬间可能丢；已 UnInit 时内部守卫直接跳过
            RoomManager.ReportDeviceOffline();
            SocketSystem.Clear();
        }


    }


}


