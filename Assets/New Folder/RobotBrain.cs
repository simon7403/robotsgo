using System.Linq;
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
    private Vector3[] _allBotPositions;
    private bool runnerLocationKnown = false;
    private bool chasing = false;
    private int runnerId;
    private string gameState;
    private Rigidbody _rb;

    public Vector3 testTarget;
    public bool testDrive = false;

    enum GameState { Idle, Start, Stop, Seen, Chase, Ready }
    GameState _gameState;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.position = new Vector3(transform.position.x, 0f, transform.position.z);
        ROSConnection.GetOrCreateInstance().Subscribe<PoseArrayMsg>("/unity/robots/pos", OnRobotPositions);
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/game/command", OnGameCommand);
    }

    void OnRobotPositions(PoseArrayMsg msg)
    {
        _totalRobots = msg.poses.Length;
        _allBotPositions = new Vector3[msg.poses.Length];

        for (int i = 0; i < _totalRobots; i++)
        {
            var p = msg.poses[i].position;
            _allBotPositions[i] = new Vector3((float)p.x, 0f, (float)p.z);
        }

        _runnerPos = _allBotPositions[runnerId];
    }

    void OnGameCommand(StringMsg msg)
    {
        if (int.TryParse(msg.data, out int runnerId))
        {
            this.runnerId = runnerId;
            return;
        }

        _gameState = msg.data switch
        {
            "START" => GameState.Start,
            "STOP" => GameState.Stop,
            "SEEN" => GameState.Seen,
            "CHASE" => GameState.Chase,
            "READY" => GameState.Ready,
            _ => _gameState
        };

        Debug.Log($"Game state: {_gameState}");
    }

    void FixedUpdate()
    {
        if (testDrive)
        {
            driveTo(testTarget);
            return;
        }

        if (_gameState == GameState.Idle || _gameState == GameState.Stop) return;

        if (id == runnerId)
            runnerBrain(); // altijd actief bij READY en START
        //else if (_gameState != GameState.Ready)
        //    hunterBrain(); // hunters wachten tijdens READY
    }

    void runnerBrain()
    {
        Vector3 myPos = transform.position;
        Vector3 fleeTarget = Vector3.zero;
        bool hunterNearby = false;

        // check of een hunter dichtbij is
        for (int i = 0; i < _allBotPositions.Length; i++)
        {
            if (i == runnerId) continue;
            if (Vector3.Distance(myPos, _allBotPositions[i]) < 3f)
            {
                hunterNearby = true;
                fleeTarget += myPos - _allBotPositions[i]; // weg van elke dichtbije hunter
            }
        }

        if (hunterNearby)
        {
            driveTo(myPos + fleeTarget.normalized * 3f);
            return;
        }

        // geen hunter dichtbij, ga naar dichtstbijzijnde hoek
        Vector3[] corners = {
        new Vector3(0, 0, 0),
        new Vector3(10, 0, 0),
        new Vector3(0, 0, 10),
        new Vector3(10, 0, 10)
    };

        Vector3 closestCorner = corners.OrderBy(c => Vector3.Distance(myPos, c)).First();

        if (Vector3.Distance(myPos, closestCorner) < 0.5f)
            return; // al in hoek, chillen

        driveTo(closestCorner);
    }

    //void hunterBrain()
    //{
    //    if (runnerLocationKnown)
    //    {
    //        nextPoint(getPoint());
    //    }
    //    else
    //    {
    //        roam();
    //    }
    //}
    //void roam()
    //{
    //    driveTo(nextPoint()); //random point closeby
    //    lookAround();
    //    if (gameState == "seen") runnerLocationKnown = true;
    //}

    //Vector3 getPoint()
    //{
    //    //calc all points around runner
    //    //sort from closest one to me
    //    //claim it
    //    //publish it
    //    //kijk naar alle /robot_{id}/claim
    //    Vector3 myPoint;
    //    if () //claimed point is taken by robot with lower ID
    //    {
    //        for (int i = 0; i < _totalRobots * 2; i++)
    //        {
    //            //check next point in array
    //        }
    //    }
    //    return myPoint;
    //}
    //Vector3 nextPoint(Vector3 target)
    //{
    //    //TODO pathfinding 
    //}

    void lookAround()
    {
        //TODO look around 
    }

    void driveTo(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        if (dir.magnitude < 0.2f)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            return;
        }

        float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(transform.eulerAngles.y, targetAngle);

        _rb.angularVelocity = new Vector3(0, angleDiff * 0.05f, 0);

        // alleen rijden als hij al roughly de goede kant op wijst
        if (Mathf.Abs(angleDiff) < 20f)
            _rb.linearVelocity = new Vector3(transform.forward.x, 0, transform.forward.z) * 2f;
        else
            _rb.linearVelocity = Vector3.zero;
    }
}