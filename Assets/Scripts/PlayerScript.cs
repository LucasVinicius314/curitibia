using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : MonoBehaviour
{
  Vector2 movementInput;
  Vector2 lookInput;
  CharacterController? controller;
  Transform? cameraContainer;
  float cameraContainerXRotation = 0f;

  public void Look(InputAction.CallbackContext context)
  {
    lookInput = context.ReadValue<Vector2>();
  }

  public void Move(InputAction.CallbackContext context)
  {
    movementInput = context.ReadValue<Vector2>();
  }

  void Awake()
  {
    controller = GetComponent<CharacterController>();
    cameraContainer = transform.Find("CameraContainer");
  }

  void Start()
  {
    Cursor.visible = false;
    Cursor.lockState = CursorLockMode.Locked;
  }

  void Update()
  {
    var vector = transform.right * movementInput.x + transform.forward * movementInput.y;

    controller?.SimpleMove(Vector3.ClampMagnitude(vector, 1f) * 3f);
  }

  void LateUpdate()
  {
    const float sensitivity = 10f;

    cameraContainerXRotation = Mathf.Clamp(cameraContainerXRotation - lookInput.y * Time.deltaTime * sensitivity, -90f, 90f);

    if (cameraContainer != null)
    {
      cameraContainer.localRotation = Quaternion.Euler(Vector2.right * cameraContainerXRotation);
    }

    transform.Rotate(Vector3.up, lookInput.x * Time.deltaTime * sensitivity);
  }
}
