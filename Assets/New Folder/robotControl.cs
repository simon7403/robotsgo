using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using UnityEngine.InputSystem;

public class RobotControl : MonoBehaviour
{
    public float wheelbase = 0.2f;
    public float manualSpeed = 2.0f;
    public float manualTurnSpeed = 2.0f;
    public float jumpForce = 5.0f;
    public float groundCheckDistance = 0.1f;

    private Rigidbody rb;
    private float linearSpeed = 0f;
    private float angularSpeed = 0f;
    private float verticalSpeed = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>("/cmd_vel", DriveCallback);
    }

    void DriveCallback(TwistMsg msg)
    {
        linearSpeed = (float)msg.linear.x;
        angularSpeed = (float)msg.angular.z;
        verticalSpeed = (float)msg.linear.y;
    }

    bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance + 0.05f);
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

        // Springen: alleen één keer per druk, en alleen als je op de grond staat
        if (keyboard.spaceKey.wasPressedThisFrame && IsGrounded())
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            manualInput = true;
        }

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