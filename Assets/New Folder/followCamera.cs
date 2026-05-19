using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 0.5f;
    public float height = 0.3f;
    public float smoothSpeed = 5f;

    void FixedUpdate()
    {
        Vector3 desiredPosition = target.position + target.forward * distance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 0.1f);
    }
}