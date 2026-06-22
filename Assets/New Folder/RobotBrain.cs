using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class RobotBrain : MonoBehaviour
{
    #region Configuration

    [Header("Identity")]
    public int id = 0;

    [Header("Drive")]
    [SerializeField] private float MaxSpeed = 2.0f;
    [SerializeField] private float RunnerSpeed = 12.0f;
    [SerializeField] private float HunterSpeed = 2.0f;

    [Header("Potential Field — Attraction")]
    [SerializeField] private float GoalAttractionStrength = 2.0f;
    [SerializeField] private float MinDistanceToGoal = 0.3f;

    [Header("Potential Field — Repulsion")]
    [SerializeField] private float RobotRepulsionStrength = 1.5f;
    [SerializeField] private float RobotInfluenceRadius = 3.0f;
    [SerializeField] private float MinRobotSeparation = 0.3f;
    [SerializeField] private float WallRepulsionStrength = 2.5f;
    [SerializeField] private float WallSafetyMargin = 1.5f;

    [Header("Jitter")]
    [SerializeField] private float JitterStrength = 0.3f;
    [SerializeField] private float JitterInterval = 0.2f;

    [Header("Runner — Corner Patrol")]
    [SerializeField] private float CornerChangeInterval = 5f;
    [SerializeField] private float HunterFleeRadius = 3f;

    [Header("Debug")]
    public Vector3 testTarget;
    public bool testDrive = false;

    #endregion

    #region State

    private int _runnerId = -1;
    private bool _runnerVisible = false;
    private Vector3 _runnerPos;
    private Dictionary<int, Vector3> _botPositions = new Dictionary<int, Vector3>();

    private Rigidbody _rb;
    private Vector3 _startPos;

    // Runner state
    private int _currentCornerIndex = -1;
    private float _lastCornerChangeTime = 0f;

    //private readonly Vector3[] _corners =
    //{
    //    new Vector3(1, 0, 1), new Vector3(5, 0, 1), new Vector3(9, 0, 1),
    //    new Vector3(1, 0, 5), new Vector3(5, 0, 5), new Vector3(9, 0, 5),
    //    new Vector3(1, 0, 9), new Vector3(5, 0, 9), new Vector3(9, 0, 9),
    //};

    private readonly Vector3[] _corners =
    {
        new Vector3(1, 0, 5), new Vector3(9, 0, 5), new Vector3(5, 0, 1), new Vector3(5, 0, 9)
    };


    // Hunter state
    private Vector3 _roamTarget;
    private bool _hasRoamTarget = false;

    // Jitter
    private Vector3 _currentJitter = Vector3.zero;
    private float _lastJitterTime = 0f;

    private enum GameState { Idle, Start, Pause, Ready }
    private GameState _gameState = GameState.Idle;

    private ROSConnection _ros;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        _startPos = transform.position;

        _rb = GetComponent<Rigidbody>();
        _rb.position = new Vector3(_startPos.x, 0f, _startPos.z);

        _ros = ROSConnection.GetOrCreateInstance();
        _ros.RegisterPublisher<Int32Msg>("/game/robots/ready");
        _ros.Subscribe<PoseArrayMsg>("/robots/pos", OnRobotPositions);
        _ros.Subscribe<StringMsg>("/game/command", OnGameCommand);
        _ros.Subscribe<BoolMsg>("/robots/seen", OnSeen);

        PublishReady();
    }

    void FixedUpdate()
    {
        if (testDrive)
        {
            driveTo(nextPoint(testTarget));
            return;
        }

        if (_gameState == GameState.Idle || _gameState == GameState.Pause)
            return;

        if (id == _runnerId)
            RunnerBrain();
        else
            HunterBrain();
    }

    #endregion

    #region ROS Callbacks

    void OnRobotPositions(PoseArrayMsg msg)
    {
        _botPositions.Clear();

        foreach (var pose in msg.poses)
        {
            int botId = (int)pose.position.z;
            _botPositions[botId] = new Vector3(
                (float)pose.position.x,
                0f,
                (float)pose.position.y  // ROS Y → Unity Z
            );
        }

        if (_runnerId >= 0 && _botPositions.TryGetValue(_runnerId, out Vector3 runnerPos))
            _runnerPos = runnerPos;
    }

    void OnGameCommand(StringMsg msg)
    {
        string raw = msg.data.Trim();
        Debug.Log($"[RobotBrain:{id}] Command received: '{raw}'");

        if (raw.StartsWith("start "))
        {
            if (int.TryParse(raw.Substring(6).Trim(), out int newRunnerId))
            {
                _runnerId = newRunnerId;
                _gameState = GameState.Start;
                MaxSpeed = (id == _runnerId) ? RunnerSpeed : HunterSpeed;
                Debug.Log($"[RobotBrain:{id}] Game started — runner = {_runnerId}");
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
                ResetState();
                break;
        }

        Debug.Log($"[RobotBrain:{id}] State → {_gameState}");
    }

    void OnSeen(BoolMsg msg)
    {
        _runnerVisible = msg.data;
        Debug.Log($"[RobotBrain:{id}] Runner visible: {_runnerVisible}");
    }

    #endregion

    #region Game Logic

    void RunnerBrain()
    {
        Vector3 fleeDirection = Vector3.zero;
        bool hunterNearby = false;

        foreach (var kvp in _botPositions)
        {
            if (kvp.Key == _runnerId) continue;

            if (Vector3.Distance(transform.position, kvp.Value) < HunterFleeRadius)
            {
                hunterNearby = true;
                fleeDirection += transform.position - kvp.Value;
            }
        }

        if (hunterNearby)
        {
            _lastCornerChangeTime = 0f;

            // Blend flee direction with a pull toward the center
            Vector3 toCenter = new Vector3(5f, 0f, 5f) - transform.position;
            Vector3 blended = (fleeDirection.normalized + toCenter.normalized * 0.8f).normalized;

            driveTo(nextPoint(transform.position + blended * HunterFleeRadius));
            return;
        }

        bool timerExpired = (Time.time - _lastCornerChangeTime) > CornerChangeInterval;

        if (_currentCornerIndex < 0 || timerExpired)
        {
            int newIndex;
            do { newIndex = Random.Range(0, _corners.Length); }
            while (newIndex == _currentCornerIndex);

            _currentCornerIndex = newIndex;
            _lastCornerChangeTime = Time.time;
            Debug.Log($"[RobotBrain:{id}] Runner → corner {_currentCornerIndex}: {_corners[_currentCornerIndex]}");
        }

        driveTo(nextPoint(_corners[_currentCornerIndex]));
    }

    void HunterBrain()
    {
        if (_runnerVisible)
            driveTo(nextPoint(_runnerPos));
        else
            Roam();
    }

    void Roam()
    {
        if (!_hasRoamTarget || Vector3.Distance(transform.position, _roamTarget) < 0.5f)
        {
            _roamTarget = new Vector3(Random.Range(1f, 9f), 0f, Random.Range(1f, 9f));
            _hasRoamTarget = true;
            Debug.Log($"[RobotBrain:{id}] New roam target: {_roamTarget}");
        }

        driveTo(nextPoint(_roamTarget));
    }

    #endregion

    #region Movement

    Vector3 nextPoint(Vector3? target)
    {
        Vector3 pos = transform.position;
        Vector3 force = Vector3.zero;

        // 1. Goal attraction
        if (target.HasValue)
        {
            Vector3 toTarget = target.Value - pos;
            toTarget.y = 0;

            if (toTarget.magnitude > MinDistanceToGoal)
            {
                force += toTarget.normalized * GoalAttractionStrength;
            }
            else
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return pos;
            }
        }

        // 2. Robot repulsion
        foreach (var kvp in _botPositions)
        {
            if (kvp.Key == id || kvp.Key == _runnerId) continue;

            Vector3 away = pos - kvp.Value;
            away.y = 0;
            float dist = away.magnitude;

            if (dist > RobotInfluenceRadius) continue;

            float safeDist = Mathf.Max(dist, MinRobotSeparation);
            force += away.normalized * (RobotRepulsionStrength / safeDist);
        }

        // 3. Wall repulsion
        const float FIELD_MIN = 0f, FIELD_MAX = 10f;

        float dxMin = pos.x - FIELD_MIN, dxMax = FIELD_MAX - pos.x;
        float dzMin = pos.z - FIELD_MIN, dzMax = FIELD_MAX - pos.z;

        if (dxMin < WallSafetyMargin) force += Vector3.right * (WallRepulsionStrength / Mathf.Max(0.001f, dxMin * dxMin));
        if (dxMax < WallSafetyMargin) force += Vector3.left * (WallRepulsionStrength / Mathf.Max(0.001f, dxMax * dxMax));
        if (dzMin < WallSafetyMargin) force += Vector3.forward * (WallRepulsionStrength / Mathf.Max(0.001f, dzMin * dzMin));
        if (dzMax < WallSafetyMargin) force += Vector3.back * (WallRepulsionStrength / Mathf.Max(0.001f, dzMax * dzMax));

        // 4. Jitter
        if (Time.time - _lastJitterTime > JitterInterval)
        {
            Vector2 rand = Random.insideUnitCircle;
            _currentJitter = new Vector3(rand.x, 0, rand.y) * JitterStrength;
            _lastJitterTime = Time.time;
        }
        force += _currentJitter;

        // 5. Fallback: keep moving forward if all forces cancel
        if (force.sqrMagnitude < 0.001f)
            force = transform.forward;

        return pos + force.normalized;
    }

    void driveTo(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        if (dir.sqrMagnitude < MinDistanceToGoal * MinDistanceToGoal) return;

        float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(transform.eulerAngles.y, targetAngle);

        _rb.angularVelocity = new Vector3(0, Mathf.Clamp(angleDiff * 0.1f, -3f, 3f), 0);

        if (Mathf.Abs(angleDiff) < 30f)
        {
            float speedFactor = 1f - (Mathf.Abs(angleDiff) / 30f);
            Vector3 forwardFlat = new Vector3(transform.forward.x, 0, transform.forward.z);
            _rb.linearVelocity = forwardFlat * MaxSpeed * speedFactor;
        }
        else
        {
            _rb.linearVelocity = Vector3.zero;
        }
    }

    #endregion

    #region Helpers

    void ResetState()
    {
        _runnerId = -1;
        _runnerVisible = false;
        _hasRoamTarget = false;
        _currentCornerIndex = -1;
        _lastCornerChangeTime = 0f;
        _gameState = GameState.Idle;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.position = new Vector3(_startPos.x, 0f, _startPos.z);

        PublishReady();
    }

    void PublishReady() => _ros.Publish("/game/robots/ready", new Int32Msg { data = id });

    #endregion
}