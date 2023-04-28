using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

class PlayerScript : MonoBehaviour
{
  Vector2 movementInput;
  Vector2 lookInput;
  CharacterController? controller;

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
  }

  void Start()
  {

  }

  void Update()
  {
    var vector = transform.right * movementInput.x + transform.forward * movementInput.y;

    controller?.SimpleMove(Vector3.ClampMagnitude(vector, 1f) * 3f);
  }

  void LateUpdate()
  {
    transform.Rotate(Vector3.up, lookInput.x * Time.deltaTime * 5f);
  }
}
