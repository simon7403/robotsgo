using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class RobotBrain : MonoBehaviour
{
    public int id = 0;
    public int TurnSpeed = 2;
    private int _totalRobots = 0;
    private Vector3 _runnerPos;
    private Vector3 myPos;
    private Vector3[] _allBotPositions;
    private bool runnerVisible = false;
    private int runnerId = -1;
    private Rigidbody _rb;
    private Vector3 _startPos;


    public Vector3 testTarget;
    public bool testDrive = false;

    enum GameState { Idle, Start, Pause, Ready }
    GameState _gameState = GameState.Idle;

    private ROSConnection _ros;

    void Start()
    {
        _startPos = transform.position; // voeg dit toe
        _rb = GetComponent<Rigidbody>();
        _rb.position = new Vector3(transform.position.x, 0f, transform.position.z);

        _ros = ROSConnection.GetOrCreateInstance();

        _ros.RegisterPublisher<Int32Msg>("/game/robots/ready");
        _ros.Subscribe<PoseArrayMsg>("/unity/pos", OnRobotPositions);
        _ros.Subscribe<StringMsg>("/game/command", OnGameCommand);
        _ros.Subscribe<BoolMsg>("/robots/seen", OnSeen);

        _ros.Publish("/game/robots/ready", new Int32Msg { data = id });
    }

    void FixedUpdate()
    {
        if (testDrive)
        {
            driveTo(nextPoint(testTarget));
            return;
        }

        if (_gameState == GameState.Idle || _gameState == GameState.Pause) return;

        if (id == runnerId)
            runnerBrain();
        else
            hunterBrain();
    }

    void OnRobotPositions(PoseArrayMsg msg)
    {
        // Bepaal eerst de hoogste ID om de array groot genoeg te maken
        int maxId = 0;
        foreach (var pose in msg.poses)
            maxId = Mathf.Max(maxId, (int)pose.position.z);

        _allBotPositions = new Vector3[maxId + 1];

        foreach (var pose in msg.poses)
        {
            int botId = (int)pose.position.z;
            _allBotPositions[botId] = new Vector3(
                (float)pose.position.x,
                0f,
                (float)pose.position.y  // y in ROS = Z in Unity
            );
        }

        myPos = _allBotPositions[id];

        if (runnerId >= 0 && runnerId < _allBotPositions.Length)
            _runnerPos = _allBotPositions[runnerId];
    }

    void OnGameCommand(StringMsg msg)
    {
        string raw = msg.data.Trim();
        Debug.Log($"Received command: {raw}");

        // "start <runner_id>"
        if (raw.StartsWith("start "))
        {
            if (int.TryParse(raw.Substring(6).Trim(), out int newRunnerId))
            {
                runnerId = newRunnerId;
                _gameState = GameState.Start;
                Debug.Log($"Game start — runner is robot {runnerId}, I am robot {id}");
            }
            return;
        }

        switch (raw)
        {
            case "pause":
                _gameState = GameState.Pause;
                break;
            case "resume":
                _gameState = GameState.Start;
                break;
            case "reset":
                runnerId = -1;
                runnerVisible = false;
                _gameState = GameState.Idle;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.position = new Vector3(_startPos.x, 0f, _startPos.z);
                _ros.Publish("/game/robots/ready", new Int32Msg { data = id });
                break;
        }

        Debug.Log($"Game state: {_gameState}");
    }

    void OnSeen(BoolMsg msg)
    {
        runnerVisible = msg.data;
        Debug.Log($"Runner visible: {runnerVisible}");
    }

    void runnerBrain()
    {
        Vector3 fleeTarget = Vector3.zero;
        bool hunterNearby = false;

        for (int i = 0; i < _allBotPositions.Length; i++)
        {
            if (i == runnerId) continue;
            if (Vector3.Distance(myPos, _allBotPositions[i]) < 3f)
            {
                hunterNearby = true;
                fleeTarget += myPos - _allBotPositions[i];
            }
        }

        if (hunterNearby)
        {
            driveTo(nextPoint(myPos + fleeTarget.normalized * 3f));
            return;
        }

        // Geen hunter dichtbij: ga naar dichtstbijzijnde hoek
        Vector3 closestCorner = new Vector3(0, 0, 0);
        float closestDist = float.MaxValue;
        foreach (Vector3 corner in new Vector3[] {
        new Vector3(0, 0, 0),  new Vector3(10, 0, 0),
        new Vector3(0, 0, 10), new Vector3(10, 0, 10)
    })
        {
            float d = Vector3.Distance(myPos, corner);
            if (d < closestDist) { closestDist = d; closestCorner = corner; }
        }

        if (closestDist < 0.5f) return; // al in hoek

        driveTo(nextPoint(closestCorner));
    }

    void hunterBrain()
    {
        if (runnerVisible)
            driveTo(nextPoint(_runnerPos));
        else
            roam();
    }

    void roam()
    {
        driveTo(nextPoint(null)); //current pos
    }

    Vector3 nextPoint(Vector3? target)
    {
        Vector3 combinedForce = Vector3.zero;

        if (target.HasValue)
        {
            Vector3 toTarget = target.Value - myPos;
            if (toTarget.magnitude < 0.2f)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return myPos; // al bij target
            }
            toTarget.y = 0;
            combinedForce += toTarget.normalized * 1.5f;
        }

        if (_allBotPositions != null && _allBotPositions.Length > 0)
        {
            for (int i = 0; i < _allBotPositions.Length; i++)
            {
                if (i == id || i == runnerId) continue;

                Vector3 toHunter = myPos - _allBotPositions[i];
                toHunter.y = 0;
                float distance = toHunter.magnitude;

                if (distance < 5f && distance > 0.1f)
                {
                    combinedForce += toHunter.normalized * (1.0f / distance);
                }
            }
        }

        float minX = 0f, maxX = 10f;
        float minZ = 0f, maxZ = 10f;
        float wallAvoidanceDistance = 1.5f;
        float wallPushStrength = 1.2f;

        if (myPos.x - minX < wallAvoidanceDistance)
            combinedForce += Vector3.right * (wallPushStrength / Mathf.Max(0.1f, myPos.x - minX));
        if (maxX - myPos.x < wallAvoidanceDistance)
            combinedForce += Vector3.left * (wallPushStrength / Mathf.Max(0.1f, maxX - myPos.x));
        if (myPos.z - minZ < wallAvoidanceDistance)
            combinedForce += Vector3.forward * (wallPushStrength / Mathf.Max(0.1f, myPos.z - minZ));
        if (maxZ - myPos.z < wallAvoidanceDistance)
            combinedForce += Vector3.back * (wallPushStrength / Mathf.Max(0.1f, maxZ - myPos.z));

        Vector2 randomCircle = Random.insideUnitCircle * 0.3f;
        combinedForce += new Vector3(randomCircle.x, 0, randomCircle.y);

        if (combinedForce.sqrMagnitude < 0.01f)
        {
            combinedForce = transform.forward;
        }

        return myPos + combinedForce.normalized;
    }

    void driveTo(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(transform.eulerAngles.y, targetAngle);

        _rb.angularVelocity = new Vector3(0, angleDiff * 0.05f, 0);

        // alleen rijden als hij al roughly de goede kant op wijst
        if (Mathf.Abs(angleDiff) < 10f)
            _rb.linearVelocity = new Vector3(transform.forward.x, 0, transform.forward.z) * 2f;
        else
            _rb.linearVelocity = Vector3.zero;
    }
}