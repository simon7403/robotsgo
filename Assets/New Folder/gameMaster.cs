using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class GameMaster : MonoBehaviour
{
    public int runnerIdToStart = 0; // Stel in via inspector welke robot de runner wordt

    private ROSConnection _ros;

    void Start()
    {
        _ros = ROSConnection.GetOrCreateInstance();
        _ros.RegisterPublisher<StringMsg>("/game/command");
    }

    void Update()
    {
        // S = start, R = reset
        if (Input.GetKeyDown(KeyCode.S))
            SendStart();
        if (Input.GetKeyDown(KeyCode.R))
            SendReset();
    }

    public void SendStart()
    {
        _ros.Publish("/game/command", new StringMsg { data = $"start {runnerIdToStart}" });
        Debug.Log($"Sent: start {runnerIdToStart}");
    }

    public void SendReset()
    {
        _ros.Publish("/game/command", new StringMsg { data = "reset" });
        Debug.Log("Sent: reset");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 200, 120));
        GUILayout.Label($"Runner ID: {runnerIdToStart}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-")) runnerIdToStart = Mathf.Max(0, runnerIdToStart - 1);
        if (GUILayout.Button("+")) runnerIdToStart++;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Start (S)")) SendStart();
        if (GUILayout.Button("Reset (R)")) SendReset();

        GUILayout.EndArea();
    }
}