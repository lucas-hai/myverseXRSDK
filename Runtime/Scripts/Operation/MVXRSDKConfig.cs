
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal class MVXRSDKConfig
    {
        public const string API_DEVEICE_URL = "v1/open/api/device?id=";
        public const string API_GET_IPCONFIG = "vrcontrol/frontend/v1/server/ip";
        public const string API_DEDUCTION = "vrcontrol/frontend/v1/customer/intergral/deduction";

        public const string API_GET_CONFIG = "v1/vr/service/config";


        // public const string HTTP_RELEASE_IP = "https://app.myverse.fans/";
        public const string HTTP_IP = "http://localhost:8868/";

        public const float NORMAL_DISTANCE = 2f;



        public const string OBSTACLE_PREFAB_OVAL_PATH = "Obstacle/Prefabs/OvalObstacle";
        public const string OBSTACLE_PREFAB_RECT_PATH = "Obstacle/Prefabs/RectObstacle";
        public const string CHARACTER_PREFAB_PATH = "Characters/Prefabs/Role";


        public const string KEYNAME_OBSTACLE_OVAL = "ObstacleOval";
        public const string KEYNAME_OBSTACLE_RECT = "ObstacleRect";

        public const string KEYNAME_CHARACTER = "Character";



    }
}

