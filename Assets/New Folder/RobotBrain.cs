using NUnit.Framework.Constraints;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.Rendering;

public class RobotBrain : MonoBehaviour
{
    public int id = 0;
    private int _totalRobots = 0;
    private Vector3 _runnerPos;
    private bool runnerLocationKnown = false;
    private bool chasing = false;
    private int runnerId;
    private string gameState;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<PoseArrayMsg>("/unity/robots/pos", OnRobotPositions);
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/game/command", OnGameCommand);
    }

    void OnRobotPositions(PoseArrayMsg msg)
    {
        _totalRobots = msg.poses.Length;
    }

    void OnGameCommand(StringMsg msg)
    {
        string data = msg.data;

        if (int.TryParse(data, out int commandId))
        {
            Debug.Log($"Runner is robot {commandId}");
            runnerId = commandId;
            return;
        }
        switch (data)
        {
            case "Start":
                Debug.Log("Game Started!");
                break;
            case "Stop":
                Debug.Log("Game Stopped!");
                break;
            case "seen":
                Debug.Log("Runner seen!");
                runnerLocationKnown = true;
                break;
            case "chase":
                Debug.Log("Chasing runner!");
                chasing = true;
                break;
        }
    }
    void FixedUpdate()
    {
        if (gameState == "stop") return;
        else if (id == runnerId) runnerBrain();
        else hunterBrain();
    }

    void runnerBrain()
    {
        if () //my pos +- iets == any other robot pos
        {
            driveTo(nextPoint()); //away from other robot
        } else if () //location in corner
        {
            //roaming of chilling random maybe idc
        }
        else
        {
            driveTo(nextPoint()); //closest corner
        }
    }

    void hunterBrain()
    {
        if (runnerLocationKnown)
        {
            nextPoint(getPoint());
        }
        else
        {
            roam();
        }
    }
    void roam()
    {
        driveTo(nextPoint()); //random point closeby
        lookAround();
        if (gameState == "seen") runnerLocationKnown = true;
    }

    Vector3 getPoint()
    {
        //calc all points around runner
        //sort from closest one to me
        //claim it
        //publish it
        //kijk naar alle /robot_{id}/claim
        Vector3 myPoint;
        if () //claimed point is taken by robot with lower ID
        {
            for (int i = 0; i < _totalRobots * 2; i++)
            {
                //check next point in array
            }
        }
            return myPoint;
    }
    Vector3 nextPoint(Vector3 target)
    {
        //TODO pathfinding 
        
    }

    void lookAround()
    {
        //TODO look around 
    }

    void driveTo(Vector3 target)
    {
        //TODO drive to target
    }
}