using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : NetworkBehaviour
{
  [SerializeField]
  InputActionAsset? inputActions;
  InputAction? lookAction;
  Vector2 c;
  Vector2 movementInput;
  Vector2 smoothedMovementInput;
  Vector2 smoothedVelocity;
  Vector2 lookInput;
  Rigidbody? rb;
  Transform? cameraContainer;
  float cameraContainerXRotation = 0f;
  [SerializeField]
  LayerMask g;

  Transform? legR;
  Transform? legL;
  Transform? root;
  Transform? chest;
  Transform? arms;
  float animationRandomiser;

  bool CheckGroundContact()
  {
    var radius = .5f;
    var checkHeight = .2f;
    var originOffset = Vector3.down * .9f;

    var checkOffset = .1f * Vector3.down;
    var origin = transform.position + originOffset;

    var d = Vector3.down;

    Debug.DrawLine(origin, origin + d * checkHeight, Color.red);
    Debug.DrawLine(origin + Vector3.left * radius, origin + Vector3.right * radius, Color.red);
    Debug.DrawLine(origin + Vector3.forward * radius, origin + Vector3.back * radius, Color.red);
    Debug.DrawLine(origin + d * checkHeight + Vector3.left * radius, origin + d * checkHeight + Vector3.right * radius, Color.red);
    Debug.DrawLine(origin + d * checkHeight + Vector3.forward * radius, origin + d * checkHeight + Vector3.back * radius, Color.red);

    var a = Physics.CheckCapsule(origin + Vector3.up * radius, origin + Vector3.down * checkHeight + Vector3.down * radius, radius, g);

    var b = Physics.CheckBox(origin + Vector3.down * checkHeight / 2, Vector3.one * checkHeight, Quaternion.identity, g);

    // Debug.Log($"{a} {b}");

    return a && b;
  }

  void AnimateRunning()
  {
    if (legR == null || legL == null || root == null || arms == null || rb == null || chest == null) return;
    float playerSpeed = new Vector3(rb.velocity.x, 0, rb.velocity.z).magnitude;
    float animationSin = Mathf.Sin((Time.time + animationRandomiser) * 15f);
    legR.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    legL.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    chest.localRotation = Quaternion.Euler(playerSpeed * 4, animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 4, 0);
    arms.localRotation = Quaternion.Euler(0, -animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 15, 0);
    if (rb.velocity.magnitude > .5f)
    {
      c = new Vector2(rb.velocity.x, rb.velocity.z);
    }
    smoothedVelocity = Vector2.Lerp(smoothedVelocity, c, .04f);
    root.rotation = Quaternion.Euler(Vector3.up * Mathf.Atan2(smoothedVelocity.x, smoothedVelocity.y) * Mathf.Rad2Deg);
    Debug.Log(playerSpeed);
  }

  void Jump()
  {
    if (rb != null)
    {
      rb.AddForce(new Vector3
      {
        x = rb.velocity.x,
        y = 5f,
        z = rb.velocity.z,
      },
      ForceMode.Impulse
      );
    }
  }

  public void Jump(InputAction.CallbackContext context)
  {
    if (!isLocalPlayer)
    {
      return;
    }

    if (context.performed && CheckGroundContact())
    {
      Jump();
    }
  }

  public void Move(InputAction.CallbackContext context)
  {
    if (!isLocalPlayer)
    {
      return;
    }

    movementInput = context.ReadValue<Vector2>();
  }

  // Mirror Events.

  public override void OnStartLocalPlayer()
  {
    GetComponent<PlayerInput>().enabled = true;
    rb = GetComponent<Rigidbody>();
    cameraContainer = transform.Find("CameraContainer");

    root = transform.Find("Model/Root");
    legR = transform.Find("Model/Root/Legs/Leg_R");
    legL = transform.Find("Model/Root/Legs/Leg_L");
    arms = transform.Find("Model/Root/Chest/Arms");
    chest = transform.Find("Model/Root/Chest");

    cameraContainer.Find("Main Camera").GetComponent<Camera>().enabled = true;

    animationRandomiser = Random.Range(0, 50);

    if (inputActions != null)
    {
      lookAction = inputActions["Look"];
    }

    Cursor.visible = false;
    Cursor.lockState = CursorLockMode.Locked;

    base.OnStartLocalPlayer();
  }

  // Unity Events.

  void OnCollisionEnter(Collision collision)
  {
    if (!isLocalPlayer)
    {
      return;
    }

    foreach (var item in collision.contacts)
    {
      Debug.DrawRay(item.point, item.normal, Color.green, 1f);
      if (collision.transform.tag == "Enemy" && rb != null)
      {
        rb.AddForce((item.normal * 2 + Vector3.up).normalized * 2, ForceMode.Impulse);
        return;
      }
    }
  }

  void Update()
  {
    if (!isLocalPlayer || rb == null)
    {
      return;
    }

    AnimateRunning();

    smoothedMovementInput = Vector2.Lerp(smoothedMovementInput, movementInput, .1f);

    var vector = transform.right * smoothedMovementInput.x + transform.forward * smoothedMovementInput.y;

    if (CheckGroundContact())
    {
      rb.velocity = Vector3.ClampMagnitude(vector, 1f) * 5f + Vector3.up * rb.velocity.y;
    }
  }

  void LateUpdate()
  {
    if (!isLocalPlayer || lookAction == null)
    {
      return;
    }

    var lookInput = lookAction.ReadValue<Vector2>();

    const float sensitivity = 10f;

    cameraContainerXRotation = Mathf.Clamp(cameraContainerXRotation - lookInput.y * Time.deltaTime * sensitivity, -90f, 90f);

    if (cameraContainer != null)
    {
      cameraContainer.localRotation = Quaternion.Euler(Vector2.right * cameraContainerXRotation);
    }

    transform.Rotate(Vector3.up, lookInput.x * Time.deltaTime * sensitivity);
  }
}
