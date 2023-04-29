using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : MonoBehaviour
{
  [SerializeField]
  InputActionAsset? inputActions;
  InputAction? lookAction;
  Vector2 movementInput;
  Vector2 lookInput;
  CharacterController? controller;
  Rigidbody? rb;
  Transform? cameraContainer;
  float cameraContainerXRotation = 0f;
  bool isGrounded = false;

  void DisablePhysics()
  {
    if (controller != null && rb != null)
    {
      isGrounded = true;
      controller.enabled = true;
      rb.isKinematic = true;
    }
  }

  void EnablePhysics(bool jump)
  {
    if (controller != null && rb != null)
    {
      isGrounded = false;
      controller.enabled = false;
      rb.isKinematic = false;

      if (jump)
      {
        rb.AddForce(new Vector3
        {
          x = controller.velocity.x,
          y = 5f,
          z = controller.velocity.z,
        }, ForceMode.Impulse);
      }
    }
  }

  public void Jump(InputAction.CallbackContext context)
  {
    if (isGrounded)
    {
      EnablePhysics(true);
    }
  }

  public void Look(InputAction.CallbackContext context)
  {
    // lookInput = context.ReadValue<Vector2>();
  }

  public void Move(InputAction.CallbackContext context)
  {
    movementInput = context.ReadValue<Vector2>();
  }

  void OnCollisionEnter(Collision collision)
  {
    if (!isGrounded)
    {
      DisablePhysics();
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

    EnablePhysics(false);
  }

  void Update()
  {
    var vector = transform.right * movementInput.x + transform.forward * movementInput.y;

    if (controller != null && rb != null)
    {
      // TODO: fix
      // if (isGrounded && !controller.isGrounded)
      // {
      //   EnablePhysics(false);
      // }

      if (isGrounded)
      {
        controller.SimpleMove(Vector3.ClampMagnitude(vector, 1f) * 3f);
      }
      else
      {
        rb.AddForce(vector);
      }
    }

    if (lookAction != null)
    {
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
}
