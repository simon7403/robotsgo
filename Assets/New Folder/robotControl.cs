using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using UnityEngine.InputSystem;

public class RobotControl : MonoBehaviour
{
    public float wheelbase = 0.2f;
    public float manualSpeed = 2.0f;
    public float manualTurnSpeed = 2.0f;
    private Rigidbody rb;
    private float linearSpeed = 0f;
    private float angularSpeed = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>("/cmd_vel", DriveCallback);
    }

    void DriveCallback(TwistMsg msg)
    {
        linearSpeed = (float)msg.linear.x;
        angularSpeed = (float)msg.angular.z;
    }

    void FixedUpdate()
    {
        var keyboard = Keyboard.current;
        bool manualInput = false;
        float currentLinear = 0f;
        float currentAngular = 0f;

        if (keyboard.wKey.isPressed) { currentLinear = manualSpeed; manualInput = true; }
        if (keyboard.sKey.isPressed) { currentLinear = -manualSpeed; manualInput = true; }
        if (keyboard.aKey.isPressed) { currentAngular = -manualTurnSpeed; manualInput = true; }
        if (keyboard.dKey.isPressed) { currentAngular = manualTurnSpeed; manualInput = true; }

        if (manualInput)
        {
            Vector3 velocity = transform.forward * currentLinear;
            rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
            rb.angularVelocity = new Vector3(0, currentAngular, 0);
            return;
        }

        currentLinear = linearSpeed;
        currentAngular = angularSpeed;
    }
}