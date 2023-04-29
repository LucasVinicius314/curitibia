using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : MonoBehaviour
{
  [SerializeField]
  InputActionAsset? inputActions;
  InputAction? lookAction;
  Vector2 movementInput;
  Vector2 smoothedMovementInput;
  Vector2 lookInput;
  CharacterController? controller;
  Rigidbody? rb;
  Transform? cameraContainer;
  float cameraContainerXRotation = 0f;
  bool isGrounded = false;

  void AirborneFalse()
  {
    if (controller != null && rb != null)
    {
      isGrounded = true;
      controller.enabled = true;
      rb.isKinematic = true;
    }
  }

  void AirborneTrue()
  {
    if (controller != null && rb != null)
    {
      isGrounded = false;
      controller.enabled = false;
      rb.isKinematic = false;
    }
  }

  bool CheckGroundContact()
  {
    var radius = .3f;
    var offset = Vector3.down * .8f;

    var pos = transform.position + offset;

    Debug.DrawLine(pos + Vector3.up * radius, pos + Vector3.down * radius, Color.red);
    Debug.DrawLine(pos + Vector3.left * radius, pos + Vector3.right * radius, Color.red);
    Debug.DrawLine(pos + Vector3.forward * radius, pos + Vector3.back * radius, Color.red);

    return Physics.CheckSphere(pos, radius);
  }

  void Jump()
  {
    if (rb == null || controller == null) return;

    rb.AddForce(new Vector3
    {
      x = controller.velocity.x,
      y = 5f,
      z = controller.velocity.z,
    }, ForceMode.Impulse);
  }

  public void Jump(InputAction.CallbackContext context)
  {
    if (isGrounded)
    {
      AirborneTrue();

      Jump();
    }
  }

  public void Move(InputAction.CallbackContext context)
  {
    movementInput = context.ReadValue<Vector2>();
  }

  void OnCollisionEnter(Collision collision)
  {
    if (!isGrounded)
    {
      var shouldSetAirborneFalse = false;

      foreach (var item in collision.contacts)
      {
        Debug.DrawRay(item.point, item.normal, Color.green);

        if (item.normal.normalized.y > .6f)
        {
          shouldSetAirborneFalse = true;

          break;
        }
      }

      // TODO: CheckGroundContact()
      if (shouldSetAirborneFalse)
      {
        AirborneFalse();
      }
    }
  }

  void Awake()
  {
    rb = GetComponent<Rigidbody>();
    controller = GetComponent<CharacterController>();
    cameraContainer = transform.Find("CameraContainer");

    if (inputActions != null)
    {
      lookAction = inputActions["Look"];
    }
  }

  void Start()
  {
    Cursor.visible = false;
    Cursor.lockState = CursorLockMode.Locked;

    AirborneTrue();
  }

  void Update()
  {
    smoothedMovementInput = Vector2.Lerp(smoothedMovementInput, movementInput, .1f);

    var vector = transform.right * smoothedMovementInput.x + transform.forward * smoothedMovementInput.y;

    if (controller != null && rb != null)
    {
      if (isGrounded && !CheckGroundContact())
      {
        AirborneTrue();
      }

      if (isGrounded)
      {
        controller.SimpleMove(Vector3.ClampMagnitude(vector, 1f) * 3f);
      }
      else
      {
        var strafeMultiplier = (controller.velocity.normalized - vector).magnitude;

        rb.AddForce(Vector3.ClampMagnitude(vector, 1f) * strafeMultiplier);
      }
    }
  }

  void LateUpdate()
  {
    if (lookAction == null)
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
