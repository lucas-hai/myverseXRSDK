using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal class SpaceObstacles : MonoBehaviour
    {


        private ObstacleType m_ObstacleType;
        private float m_ObstacleRadius;
        private float m_ObstacleLength;
        private float m_ObstacleWidth;




        [Header("是否开启距离检测")]
        [SerializeField]
        private bool m_EnableDisMap;

        /// <summary>
        /// 距离检测阈值
        /// </summary> 
        [Header("距离检测阈值")]
        [SerializeField]
        private float m_DisMapThreshold = 1f;

        private GameObject m_Container;
        private float m_CachedThresholdSqr;

        private Vector2 m_ObstaclePos;
        private Vector2 m_XRPos;

        void Awake()
        {
            m_Container = transform.GetChild(0).gameObject;
        }

        void OnEnable()
        {
            // SelfTransform 改为每帧实时查询，支持自身节点运行时注册/热替换/注销
            MonoSystem.AddLateUpdateListener(OnLateUpdate);
        }

        void OnDisable()
        {
            MonoSystem.RemoveLateUpdateListener(OnLateUpdate);
        }

        void OnDestroy()
        {
            MonoSystem.RemoveLateUpdateListener(OnLateUpdate);
        }

        public void SetObstacleInfo(ObstacleType type, float radius, float length, float height)
        {
            m_ObstacleType = type;
            m_ObstacleRadius = radius;
            m_ObstacleLength = length;
            m_ObstacleWidth = height;

            // 预算障碍物补偿平方值，避免 LateUpdate 每帧重算
            float compensationSqr = type == ObstacleType.Oval
                ? radius * radius
                : Mathf.Max(length, height) * Mathf.Max(length, height);
            m_CachedThresholdSqr = m_DisMapThreshold * m_DisMapThreshold + compensationSqr;
        }



        private void OnLateUpdate()
        {
            if (!m_EnableDisMap || m_Container == null)
            {
                return;
            }
            DistanceDetection();
        }

        private void DistanceDetection()
        {
            var xr = MVXRSDK.SelfTransform;
            if (xr == null) return;

            m_ObstaclePos.x = transform.localPosition.x;
            m_ObstaclePos.y = transform.localPosition.z;
            m_XRPos.x = xr.localPosition.x;
            m_XRPos.y = xr.localPosition.z;

            float dx = m_ObstaclePos.x - m_XRPos.x;
            float dy = m_ObstaclePos.y - m_XRPos.y;
            float distSqr = dx * dx + dy * dy;

            m_Container.SetActive(distSqr <= m_CachedThresholdSqr);
        }


    }
}