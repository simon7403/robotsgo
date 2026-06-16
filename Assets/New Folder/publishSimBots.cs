using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;

public class PublishSimBots : MonoBehaviour
{
    [SerializeField] GameObject allBots;
    public float publishRate = 1f;

    private float _timer = 0f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().RegisterPublisher<PoseArrayMsg>("/unity/pos");
    }

    void FixedUpdate()
    {
        _timer += Time.deltaTime;
        if (_timer < publishRate) return;
        _timer = 0f;

        Publish();
    }

    void Publish()
    {
        var poses = new List<PoseMsg>();

        foreach (Transform child in allBots.transform)
        {
            var brain = child.GetComponent<RobotBrain>();
            if (brain == null) continue;

            var pos = child.position;

            // Unity Y-up naar ROS2 Z-up: yaw in Unity = -yaw in ROS2
            float yawUnity = child.eulerAngles.y * Mathf.Deg2Rad;
            float yawRos = -yawUnity;
            float halfYaw = yawRos * 0.5f;

            poses.Add(new PoseMsg
            {
                position = new PointMsg(pos.x, pos.z, brain.id),
                orientation = new QuaternionMsg(0, 0, Mathf.Sin(halfYaw), Mathf.Cos(halfYaw))
            });
        }

        var msg = new PoseArrayMsg
        {
            header = new HeaderMsg { frame_id = "unity" },
            poses = poses.ToArray()
        };

        ROSConnection.GetOrCreateInstance().Publish("/unity/pos", msg);
        print($"Published {msg} robot positions");
    }
}