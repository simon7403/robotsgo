using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class TOFSensor : MonoBehaviour
{
    public float maxRange = 2.0f;
    public float publishRate = 10f;
    private float timer = 0f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().RegisterPublisher<RangeMsg>("/tof_sensor");
    }

    void FixedUpdate()
    {
        timer += Time.deltaTime;
        if (timer >= 1f / publishRate)
        {
            timer = 0f;
            PublishDistance();
        }
    }

    void PublishDistance()
    {
        RaycastHit hit;
        float distance;

        if (Physics.Raycast(transform.position, transform.forward, out hit, maxRange))
            distance = hit.distance;
        else
            distance = maxRange;

        RangeMsg msg = new RangeMsg();
        msg.range = distance;
        msg.max_range = maxRange;
        msg.min_range = 0.03f;
        ROSConnection.GetOrCreateInstance().Publish("/tof_sensor", msg);
    }
}