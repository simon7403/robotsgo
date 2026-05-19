using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Camera mainCamera;
    public Camera followCamera;

    void Start()
    {
        mainCamera.enabled = true;
        followCamera.enabled = false;
    }

    void FixedUpdate()
    {
        if (UnityEngine.InputSystem.Keyboard.current.zKey.wasPressedThisFrame)
        {
            mainCamera.enabled = !mainCamera.enabled;
            followCamera.enabled = !followCamera.enabled;
        }
    }
}