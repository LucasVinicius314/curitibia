using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : NetworkBehaviour
{
  [SerializeField]
  InputActionAsset? inputActions;
  InputAction? lookAction;
  Vector2 movementInput;
  Vector2 smoothedMovementInput;
  Vector2 lookInput;
  Rigidbody? rb;
  Transform? cameraContainer;
  float cameraContainerXRotation = 0f;
  [SerializeField]
  LayerMask g;
  public bool grounded;


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

    cameraContainer.Find("Main Camera").GetComponent<Camera>().enabled = true;
    cameraContainer.Find("Main Camera").GetComponent<AudioListener>().enabled = true;

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
    grounded = Physics.Raycast(transform.position, Vector3.down, 2 * 0.5f + 0.2f);
    if (!isLocalPlayer || rb == null)
    {
      return;
    }

    Debug.DrawLine(transform.position, Vector3.down);
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
