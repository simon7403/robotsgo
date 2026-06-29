using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;

public class SpawnPhysicalBots : MonoBehaviour
{
    [SerializeField] GameObject chariotPrefab;

    readonly Dictionary<int, GameObject> _robots = new();
    readonly Dictionary<int, Vector3> _lastPositions = new();

    const float OUTLIER_THRESHOLD = 0f;      // max delta before treating as outlier
    const float TELEPORT_THRESHOLD = 0.15f;  // above this it's a real teleport, not an outlier
    const float LERP_SPEED = 0.25f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<PoseArrayMsg>("/cam/pos", UpdateRobots);
        print("yo");
    }

    void UpdateRobots(PoseArrayMsg msg)
    {
        for (int i = 0; i < msg.poses.Length; i++)
        {
            if (!_robots.ContainsKey(i))
            {
                var robot = Instantiate(chariotPrefab, transform);
                Destroy(robot.GetComponent<Rigidbody>());
                _robots[i] = robot;
            }

            // Reactivate the robot if it was previously hidden
            if (!_robots[i].activeSelf) _robots[i].SetActive(true);

            var p = msg.poses[i].position;
            var r = msg.poses[i].orientation;

            // ROS position mapping:
            // ROS X -> Unity X
            // ROS Y -> Unity Z
            Vector3 localPosition = new Vector3((float)p.x, 0f, (float)p.y);

            // ROS orientation is a Z-yaw quaternion:
            // ROS yaw 0 degrees  = +X
            // ROS yaw 90 degrees = +Y
            //
            // Unity uses Y-axis yaw:
            // Unity yaw 0 degrees  = +Z
            // Unity yaw 90 degrees = +X
            //
            // Since ROS Y maps to Unity Z, the inverse conversion is:
            // Unity yaw = 90 degrees - ROS yaw.
            double sinyCosp = 2.0 * ((r.w * r.z) + (r.x * r.y));
            double cosyCosp = 1.0 - (2.0 * ((r.y * r.y) + (r.z * r.z)));
            double yawRosRad = System.Math.Atan2(sinyCosp, cosyCosp);
            double yawRosDeg = yawRosRad * Mathf.Rad2Deg;

            float yawUnityDeg = (float)(90.0 - yawRosDeg);
            Quaternion localRotation = Quaternion.Euler(0f, yawUnityDeg, 0f);

            // Filter and apply position
            if (_lastPositions.TryGetValue(i, out Vector3 prev))
            {
                float dist = Vector3.Distance(prev, localPosition);

                // Ignore values that are slightly off (outliers), but allow real teleports
                bool isOutlier = dist > OUTLIER_THRESHOLD && dist < TELEPORT_THRESHOLD;
                Vector3 target = isOutlier ? prev : localPosition;

                // Lerp toward target to smooth out jitter
                _robots[i].transform.localPosition = Vector3.Lerp(prev, target, LERP_SPEED);
            }
            else
            {
                // First update: place directly without lerp
                _robots[i].transform.localPosition = localPosition;
            }

            // Store the smoothed position for next frame's outlier check
            _lastPositions[i] = _robots[i].transform.localPosition;

            // Apply corrected rotation
            _robots[i].transform.localRotation = localRotation;
        }

        for (int i = msg.poses.Length; i < _robots.Count; i++)
            _robots[i].SetActive(false);
    }
}