using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;

public class SpawnPhysicalBots : MonoBehaviour
{
    [SerializeField] GameObject chariotPrefab;

    readonly Dictionary<int, GameObject> _robots = new();
    readonly Dictionary<int, Vector3> _lastPositions = new();

    const float OUTLIER_THRESHOLD = 0f;  // max delta before treating as outlier
    const float TELEPORT_THRESHOLD = 0.1f;  // above this it's a real teleport, not an outlier
    const float LERP_SPEED = 0.25f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<PoseArrayMsg>("/cam/pos", UpdateRobots);
        print("yo");
    }

    void UpdateRobots(PoseArrayMsg msg)
    {
        print(msg);
        //print($"Received {msg} robot positions");
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

            // 1. Define the local position using your mapping (ROS X -> Unity X, ROS Y -> Unity Z)
            //Vector3 localPosition = new Vector3((float)p.x + 1f, 0f, (float)p.z); for unity test
            Vector3 localPosition = new Vector3((float)p.x, 0f, (float)p.y); //for cam

            // 2. Define the local rotation
            Quaternion localRotation = new Quaternion((float)r.x, (float)r.z, (float)r.y, (float)r.w); //for cam
            //Quaternion localRotation = new Quaternion((float)r.x, (float)r.y, (float)r.z, (float)r.w); //for unity test

            // 3. Filter and apply position
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

            // 4. Apply rotation directly (relative to script's GameObject parent)
            _robots[i].transform.localRotation = localRotation;
        }

        for (int i = msg.poses.Length; i < _robots.Count; i++)
            _robots[i].SetActive(false);
    }
}