using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class RobotDrive : MonoBehaviour
{
    public int id = 0;
    public float wheelbase = 0.2f;
    
    private Rigidbody rb;
    private float linearSpeed = 0f;
    private float angularSpeed = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>($"/robot_{id}/cmd_vel", DriveCallback);
    }

    void DriveCallback(TwistMsg msg)
    {
        linearSpeed = (float)msg.linear.x;
        angularSpeed = (float)msg.angular.z;
    }

    void FixedUpdate()
    {
        Vector3 velocity = -transform.forward * linearSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
        rb.angularVelocity = new Vector3(0, angularSpeed, 0);
    }
}