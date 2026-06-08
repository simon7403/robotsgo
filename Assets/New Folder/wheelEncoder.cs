using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class WheelEncoder : MonoBehaviour
{
    public float wheelbase = 0.2f;
    public float publishRate = 10f;
    private float timer = 0f;
    private Vector3 lastPosition;
    private float lastYRotation;
    private float distanceLeft = 0f;
    private float distanceRight = 0f;

    void Start()
    {
        lastPosition = transform.position;
        lastYRotation = transform.eulerAngles.y;
        ROSConnection.GetOrCreateInstance().RegisterPublisher<Float32Msg>("/wheel_encoder_left");
        ROSConnection.GetOrCreateInstance().RegisterPublisher<Float32Msg>("/wheel_encoder_right");
    }

    void Update()
    {
        float delta = Vector3.Distance(transform.position, lastPosition);
        float deltaRotation = Mathf.DeltaAngle(lastYRotation, transform.eulerAngles.y) * Mathf.Deg2Rad;

        distanceLeft += delta - (deltaRotation * wheelbase / 2);
        distanceRight += delta + (deltaRotation * wheelbase / 2);

        lastPosition = transform.position;
        lastYRotation = transform.eulerAngles.y;

        timer += Time.deltaTime;
        if (timer >= 1f / publishRate)
        {
            timer = 0f;
            Float32Msg msgLeft = new Float32Msg();
            Float32Msg msgRight = new Float32Msg();
            msgLeft.data = distanceLeft;
            msgRight.data = distanceRight;
            ROSConnection.GetOrCreateInstance().Publish("/wheel_encoder_left", msgLeft);
            ROSConnection.GetOrCreateInstance().Publish("/wheel_encoder_right", msgRight);
        }
    }
}