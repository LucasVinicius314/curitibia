using UnityEngine;
using Mirror;

#nullable enable

public class PlayerAnimation : NetworkBehaviour
{

  [SyncVar] Vector3 rbVelocity;
  GameObject? animationLayer;
  Rigidbody? rb;
  Transform? model;
  Transform? legR;
  Transform? legL;
  Transform? arms;
  Transform? root;
  Transform? chest;
  Transform? _legR;
  Transform? _legL;
  Transform? _arms;
  Transform? _root;
  Transform? _chest;
  PlayerScript? playerScript;
  Vector3 horizontalVelocity;
  Vector3 smoothedVelocity;
  Vector3 targetDirection;
  Vector3 playerDirection;
  float lerpPower = 24f;
  float animationRandomiser;
  float animationSin;
  float playerSpeed;
  bool onGround;
  bool touchingWall;


  public override void OnStartLocalPlayer()
  {
    rb = GetComponent<Rigidbody>();
  }

  void Start()
  {
    Debug.Log("Start");
    model = transform.Find("Model");
    root = transform.Find("Model/Root");
    legR = transform.Find("Model/Root/Legs/Leg_R");
    legL = transform.Find("Model/Root/Legs/Leg_L");
    arms = transform.Find("Model/Root/Chest/Arms");
    chest = transform.Find("Model/Root/Chest");
    _root = transform.Find("Model/AnimationRoot");
    _legR = transform.Find("Model/AnimationRoot/Legs/Leg_R");
    _legL = transform.Find("Model/AnimationRoot/Legs/Leg_L");
    _arms = transform.Find("Model/AnimationRoot/Chest/Arms");
    _chest = transform.Find("Model/AnimationRoot/Chest");
    animationRandomiser = Random.Range(0, 50);
    playerScript = GetComponent<PlayerScript>();
  }

  void Update()
  {
    if (playerScript == null || rb == null || model == null) return;
    if (isLocalPlayer) SendVelocity(rb.velocity);

    model.rotation = (Quaternion.Euler(0, 0, 0));
    animationSin = Mathf.Sin((Time.time + animationRandomiser) * 15f);
    playerSpeed = new Vector3(rbVelocity.x, 0, rbVelocity.z).magnitude;

    if (rbVelocity.magnitude > 1f)
    {
      horizontalVelocity = new Vector3(rbVelocity.x, 0, rbVelocity.z).normalized * 4;
    }

    smoothedVelocity = Vector3.Lerp(smoothedVelocity, horizontalVelocity, Time.deltaTime * 3);
    targetDirection = horizontalVelocity;
    playerDirection = smoothedVelocity;
    onGround = playerScript.grounded;

    PlayerRaycasts();
    if (onGround)
    {
      OnGround();
    }
    else
    {
      OnAir();
    }

    LerpTransforms();
  }

  void RunningAnimation()
  {
    if (_legR == null || _legL == null || _root == null || _arms == null || _chest == null) return;
    _legR.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    _legL.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    _arms.localRotation = Quaternion.Euler(0, -animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 15, 0);
    _chest.localRotation = Quaternion.Euler(0, animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 4, 0);
  }

  void OnGround()
  {
    RotateTowardsVelocity();
    RunningAnimation();
  }

  void OnAir()
  {
    if (_root == null) return;
    RotateTowardsVelocity();
    if (touchingWall) RunningAnimation();
  }

  void RotateTowardsVelocity()
  {
    if (_root == null) return;

    float angle = Vector3.Angle(targetDirection, playerDirection);
    if (Vector3.Angle(targetDirection, Quaternion.AngleAxis(90, Vector3.up) * playerDirection) < 90f)
    {
      angle *= -1;
    }
    _root.rotation = Quaternion.Euler(new Vector3(playerSpeed * 4, Mathf.Atan2(smoothedVelocity.x, smoothedVelocity.z) * Mathf.Rad2Deg, Mathf.Clamp(angle, -35, 35f)));

    Debug.DrawLine(_root.position, targetDirection + _root.position, Color.red);
    Debug.DrawLine(_root.position, playerDirection + _root.position, Color.green);
    Debug.DrawLine(_root.position, Quaternion.AngleAxis(90, Vector3.up) * playerDirection + _root.position, Color.blue);
  }

  void LerpTransforms()
  {
    if (legR == null || legL == null || root == null || arms == null || chest == null) return;
    if (_legR == null || _legL == null || _root == null || _arms == null || _chest == null) return;
    Utils.LerpTransform(root, _root, lerpPower * Time.deltaTime);
    Utils.LerpTransform(legR, _legR, lerpPower * Time.deltaTime);
    Utils.LerpTransform(legL, _legL, lerpPower * Time.deltaTime);
    Utils.LerpTransform(arms, _arms, lerpPower * Time.deltaTime);
    Utils.LerpTransform(chest, _chest, lerpPower * Time.deltaTime);
  }

  void PlayerRaycasts()
  {
    if (_root == null) return;

    Vector3 origin = _root.position + Vector3.down / 2;
    float wallCheckAngle = 45;
    float wallCheckDistance = 1f;
    bool rightWall = Physics.Raycast(origin, Quaternion.AngleAxis(wallCheckAngle, Vector3.up) * playerDirection, wallCheckDistance);
    bool leftWall = Physics.Raycast(origin, Quaternion.AngleAxis(-wallCheckAngle, Vector3.up) * playerDirection, wallCheckDistance);
    if (rightWall || leftWall) touchingWall = true;
    else touchingWall = false;
    Debug.DrawLine(origin, origin + Quaternion.AngleAxis(wallCheckAngle, Vector3.up) * playerDirection.normalized * wallCheckDistance, rightWall ? Color.green : Color.magenta);
    Debug.DrawLine(origin, origin + Quaternion.AngleAxis(-wallCheckAngle, Vector3.up) * playerDirection.normalized * wallCheckDistance, leftWall ? Color.green : Color.magenta);
  }


  [Command]
  public void SendVelocity(Vector3 velocity)
  {
    rbVelocity = velocity;
    PropagateVelocity(rbVelocity);
  }

  [ClientRpc]
  public void PropagateVelocity(Vector3 velocity)
  {
    rbVelocity = velocity;
  }
}
