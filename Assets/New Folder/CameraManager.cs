using UnityEngine;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    public Camera mainCamera;
    public Camera followCamera;
    private float cooldown = 0f;

    void Start()
    {
        mainCamera.enabled = true;
        followCamera.enabled = false;
    }

    void FixedUpdate()
    {
        cooldown -= Time.deltaTime;

        if (Keyboard.current.zKey.isPressed && cooldown <= 0f)
        {
            mainCamera.enabled = !mainCamera.enabled;
            followCamera.enabled = !followCamera.enabled;
            cooldown = 0.3f;
        }
    }
}