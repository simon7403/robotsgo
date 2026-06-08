using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.VisualScripting;
using UnityEngine;

public class TOFSensor : MonoBehaviour
{
    public float maxRange = 2.0f;
    public float publishfrequency = 2f;
    private float timer = 0f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().RegisterPublisher<Float32Msg>("/tof_sensor");
    }

    void FixedUpdate()
    {
        timer += Time.deltaTime;
        if (timer >= publishfrequency)
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

        ROSConnection.GetOrCreateInstance().Publish("/tof_sensor", new Float32Msg(distance));
    }
}