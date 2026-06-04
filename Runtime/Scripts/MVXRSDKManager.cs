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
            SocketSystem.Clear();
        }


    }


}


