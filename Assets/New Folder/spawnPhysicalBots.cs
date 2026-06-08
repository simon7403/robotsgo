using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;

public class SpawnPhysicalBots : MonoBehaviour
{
    [SerializeField] GameObject chariotPrefab;

    readonly Dictionary<int, GameObject> _robots = new();

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<PoseArrayMsg>("/cam/pos", UpdateRobots);
    }

    void UpdateRobots(PoseArrayMsg msg)
    {
        print($"Received {msg} robot positions");
        for (int i = 0; i < msg.poses.Length; i++)
        {
            if (!_robots.ContainsKey(i))
            {
                var robot = Instantiate(chariotPrefab, transform);
                Destroy(robot.GetComponent<Rigidbody>());
                robot.GetComponent<RobotDrive>().id = i;
                _robots[i] = robot;
            }

            // Reactivate the robot if it was previously hidden
            if (!_robots[i].activeSelf) _robots[i].SetActive(true);

            var p = msg.poses[i].position;
            var r = msg.poses[i].orientation;

            // 1. Define the local position using your mapping (ROS X -> Unity X, ROS Y -> Unity Z)
            //Vector3 localPosition = new Vector3((float)p.x + 1f, 0f, (float)p.z); for unity test
            Vector3 localPosition = new Vector3((float)p.x, 0f, (float)p.z); //for cam

            // 2. Define the local rotation
            Quaternion localRotation = new Quaternion((float)r.x, (float)r.z, (float)r.y, (float)r.w); //for cam
            //Quaternion localRotation = new Quaternion((float)r.x, (float)r.y, (float)r.z, (float)r.w); //for unity test

            // 3. Apply them to local space relative to the script's GameObject parent
            _robots[i].transform.localPosition = localPosition;
            _robots[i].transform.localRotation = localRotation;
        }

        for (int i = msg.poses.Length; i < _robots.Count; i++)
            _robots[i].SetActive(false);
    }
}