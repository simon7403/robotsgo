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
        ROSConnection.GetOrCreateInstance().RegisterPublisher<PoseArrayMsg>("/unity/robots/pos");
    }

    void Update()
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
            var pos = child.position;
            var rot = child.rotation;

            poses.Add(new PoseMsg
            {
                position = new PointMsg(pos.x, 0f, pos.z),
                orientation = new QuaternionMsg(rot.x, rot.y, rot.z, rot.w)
            });
        }

        var msg = new PoseArrayMsg
        {
            header = new HeaderMsg { frame_id = "unity" },
            poses = poses.ToArray()
        };

        ROSConnection.GetOrCreateInstance().Publish("/unity/robots/pos", msg);
    }
}