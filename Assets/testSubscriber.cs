using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class TestSubscriber : MonoBehaviour
{
    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/test_topic", MessageReceived);
    }

    void MessageReceived(StringMsg msg)
    {
        Debug.Log("Received: " + msg.data);
    }
}