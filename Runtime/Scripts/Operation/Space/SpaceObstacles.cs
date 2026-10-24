using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>障碍物资源的被动几何视图；距离显隐与数据寿命由所属空间服务统一控制。</summary>
    internal sealed class SpaceObstacles : MonoBehaviour
    {
        internal void Apply(ObstacleUpdate obstacle)
        {
            transform.localPosition = obstacle.Position;
            transform.localRotation = Quaternion.identity;
            transform.localScale = obstacle.Shape == ObstacleShape.Box
                ? new Vector3(obstacle.Length, 3, obstacle.Width)
                : new Vector3(obstacle.Radius, 3, obstacle.Radius);
        }
    }
}
