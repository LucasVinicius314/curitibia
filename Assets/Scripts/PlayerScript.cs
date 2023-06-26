using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

#nullable enable

public enum BlockRayCastMode
{
  normal,
  antinormal,
}

class PlayerScript : NetworkBehaviour
{
  [SerializeField]
  InputActionAsset? inputActions;
  InputAction? lookAction;
  Vector2 movementInput;
  Vector2 smoothedMovementInput;
  Vector2 lookInput;
  Vector3 playerDirection;
  Rigidbody? rb;
  Transform? cameraContainer;
  TerrainScript? terrainScript;
  float cameraContainerXRotation = 0f;
  [SerializeField]
  LayerMask g;
  public bool grounded;
  ChunkScript? currentChunk;

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

  System.Collections.IEnumerator CurrentChunkRoutine()
  {
    while (true)
    {
      if (terrainScript != null)
      {
        var targetX = Mathf.FloorToInt((transform.position.x - 16) / 32 + 1);
        var targetZ = Mathf.FloorToInt((transform.position.z - 16) / 32 + 1);

        var target = (targetX, targetZ);

        var chunk = terrainScript.GetChunk(target);

        if (currentChunk != chunk)
        {
          currentChunk = chunk;

          if (chunk != null)
          {
            terrainScript.UnloadChunks(chunk.chunkCoordinate, 3);
            yield return terrainScript.LoadChunks(chunk.chunkCoordinate, 3);
          }
        }
      }

      yield return new WaitForSeconds(1f);
    }
  }

  Vector3? DoBlockRaycast(Transform cameraContainer, BlockRayCastMode blockRayCastMode)
  {
    RaycastHit hit;
    if (Physics.Raycast(cameraContainer.position, cameraContainer.forward, out hit, 6f))
    {
      var targetPoint = hit.point + hit.normal * (blockRayCastMode == BlockRayCastMode.normal ? .1f : -.1f);

      return new Vector3
      {
        x = Mathf.Floor(targetPoint.x),
        y = Mathf.Floor(targetPoint.y),
        z = Mathf.Floor(targetPoint.z),
      };
    }

    return null;
  }

  public void Fire(InputAction.CallbackContext context)
  {
    if (!isLocalPlayer || cameraContainer == null || terrainScript == null)
    {
      return;
    }

    if (context.performed)
    {
      var blockRaycast = DoBlockRaycast(cameraContainer, BlockRayCastMode.antinormal);

      if (blockRaycast == null)
      {
        return;
      }

      terrainScript.SetBlock((Vector3)blockRaycast, null);
    }
  }

  public void Fire2(InputAction.CallbackContext context)
  {
    if (!isLocalPlayer || cameraContainer == null || terrainScript == null)
    {
      return;
    }

    if (context.performed)
    {
      var blockRaycast = DoBlockRaycast(cameraContainer, BlockRayCastMode.normal);

      if (blockRaycast == null)
      {
        return;
      }

      var block = (TerrainScript.instance?.blocks ?? new List<Block>())[0];

      terrainScript.SetBlock((Vector3)blockRaycast, block);
    }
  }

  void HandleBlockHighlight(Transform cameraContainer)
  {
    var blockRaycast = DoBlockRaycast(cameraContainer, BlockRayCastMode.normal);

    if (blockRaycast == null)
    {
      return;
    }

    var origin = (Vector3)blockRaycast;

    var x0y0z1 = origin + Vector3.forward;
    var x0y1z0 = origin + Vector3.up;
    var x0y1z1 = origin + Vector3.up + Vector3.forward;
    var x1y0z0 = origin + Vector3.right;
    var x1y0z1 = origin + Vector3.right + Vector3.forward;
    var x1y1z0 = origin + Vector3.right + Vector3.up;
    var x1y1z1 = origin + Vector3.one;

    Debug.DrawLine(origin, x1y0z0);
    Debug.DrawLine(origin, x0y1z0);
    Debug.DrawLine(origin, x0y0z1);

    Debug.DrawLine(x1y0z0, x1y1z0);
    Debug.DrawLine(x1y0z0, x1y0z1);

    Debug.DrawLine(x1y1z0, x1y1z1);

    Debug.DrawLine(x0y1z0, x1y1z0);
    Debug.DrawLine(x0y1z0, x0y1z1);

    Debug.DrawLine(x0y0z1, x1y0z1);
    Debug.DrawLine(x0y0z1, x0y1z1);

    Debug.DrawLine(x1y0z1, x1y1z1);

    Debug.DrawLine(x0y1z1, x1y1z1);
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

    terrainScript = GameObject.Find("Geometry").GetComponent<TerrainScript>();

    StartCoroutine(CurrentChunkRoutine());

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

    StepClimb();

    smoothedMovementInput = Vector2.Lerp(smoothedMovementInput, movementInput, .1f);

    var vector = transform.right * smoothedMovementInput.x + transform.forward * smoothedMovementInput.y;

    if (CheckGroundContact())
    {
      rb.velocity = Vector3.ClampMagnitude(vector, 1f) * 5f + Vector3.up * rb.velocity.y;
    }
    else
    {
      rb.velocity = Vector3.ClampMagnitude(vector, 1f) * 5f + Vector3.up * rb.velocity.y;
    }

    if (cameraContainer != null)
    {
      HandleBlockHighlight(cameraContainer);
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

  void StepClimb()
  {
    if (rb == null) return;
    if (movementInput.magnitude == 0) return;
    float stepSpeed = 12f;
    float stepHeight = 1f;
    float playerRadious = 0.3f;
    float detectionAngle = 90f;
    Quaternion rightOffset = Quaternion.AngleAxis(detectionAngle / 2, Vector3.up);
    Quaternion leftOffset = Quaternion.AngleAxis(-detectionAngle / 2, Vector3.up);
    Vector3 lower = transform.position + playerDirection * playerRadious + Vector3.down * 0.89f;
    Vector3 rLower = transform.position + rightOffset * playerDirection * playerRadious + Vector3.down * 0.89f;
    Vector3 lLower = transform.position + leftOffset * playerDirection * playerRadious + Vector3.down * 0.89f;
    Vector3 upper = lower + Vector3.up * stepHeight;
    Vector3 rUpper = rLower + Vector3.up * stepHeight;
    Vector3 lUpper = lLower + Vector3.up * stepHeight;

    bool _lower = false;
    bool _upper = false;
    if (new Vector2(rb.velocity.x, rb.velocity.z).magnitude >= 1f)
    {
      playerDirection = new Vector3(rb.velocity.x, 0, rb.velocity.z).normalized;
    }

    // Check if there is a wall in front of the player
    RaycastHit hitLower;
    if (Physics.Raycast(lower, playerDirection, out hitLower, 0.4f))
    {
      RaycastHit hitUpper;
      _lower = true;
      if (!Physics.Raycast(upper, playerDirection, out hitUpper, 0.6f))
      {
        rb.useGravity = false;
        rb.position -= new Vector3(0f, -stepSpeed * Time.deltaTime, 0f);
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
      }
      else _upper = true;
    }

    // Check if there is a wall on the right diagonal
    else if (Physics.Raycast(rLower, rightOffset * playerDirection, out hitLower, 0.4f))
    {
      RaycastHit hitUpper;
      _lower = true;
      if (!Physics.Raycast(rUpper, rightOffset * playerDirection, out hitUpper, 0.6f))
      {
        rb.useGravity = false;
        rb.position -= new Vector3(0f, -stepSpeed * Time.deltaTime, 0f);
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
      }
      else _upper = true;
    }

    // Check if there is a wall on the left diagonal
    else if (Physics.Raycast(lLower, leftOffset * playerDirection, out hitLower, 0.4f))
    {
      RaycastHit hitUpper;
      _lower = true;
      if (!Physics.Raycast(lUpper, leftOffset * playerDirection, out hitUpper, 0.6f))
      {
        rb.useGravity = false;
        rb.position -= new Vector3(0f, -stepSpeed * Time.deltaTime, 0f);
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
      }
      else _upper = true;
    }

    // Turn gravity back on if player is not climbing a step
    if (!_lower || _upper)
    {
      rb.useGravity = true;
      return;
    }

    // Draw left
    Debug.DrawLine(lLower, lLower + leftOffset * playerDirection * 0.4f, _lower ? Color.green : Color.magenta);
    Debug.DrawLine(lUpper, lUpper + leftOffset * playerDirection * 0.6f, _upper ? Color.green : Color.magenta);

    // Draw right
    Debug.DrawLine(rLower, rLower + rightOffset * playerDirection * 0.4f, _lower ? Color.green : Color.magenta);
    Debug.DrawLine(rUpper, rUpper + rightOffset * playerDirection * 0.6f, _upper ? Color.green : Color.magenta);

    // Draw middle
    Debug.DrawLine(lower, lower + playerDirection * 0.4f, _lower ? Color.green : Color.magenta);
    Debug.DrawLine(upper, upper + playerDirection * 0.6f, _upper ? Color.green : Color.magenta);
  }
}
