using UnityEngine;
using Mirror;

#nullable enable

public class PlayerAnimation : NetworkBehaviour
{

  Rigidbody? rb;
  Transform? legR;
  Transform? legL;
  Transform? root;
  Transform? chest;
  Transform? arms;
  Vector3 horizontalVelocity;
  Vector3 smoothedVelocity;
  float animationRandomiser;

  public override void OnStartLocalPlayer()
  {
    rb = GetComponent<Rigidbody>();
    root = transform.Find("Model/Root");
    legR = transform.Find("Model/Root/Legs/Leg_R");
    legL = transform.Find("Model/Root/Legs/Leg_L");
    arms = transform.Find("Model/Root/Chest/Arms");
    chest = transform.Find("Model/Root/Chest");
    animationRandomiser = Random.Range(0, 50);
  }

  void Update()
  {
    AnimateRunning();
  }

  void AnimateRunning()
  {
    if (legR == null || legL == null || root == null || arms == null || rb == null || chest == null) return;
    float playerSpeed = new Vector3(rb.velocity.x, 0, rb.velocity.z).magnitude;
    float animationSin = Mathf.Sin((Time.time + animationRandomiser) * 15f);
    legR.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    legL.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(playerSpeed * 40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(playerSpeed * -40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
    arms.localRotation = Quaternion.Euler(0, -animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 15, 0);
    if (rb.velocity.magnitude > 1f)
    {
      horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
    }
    smoothedVelocity = Vector3.Lerp(smoothedVelocity, horizontalVelocity, .05f);

    Vector3 targetDirection = horizontalVelocity;
    Vector3 playerDirection = smoothedVelocity;

    float angle = Vector3.Angle(targetDirection, playerDirection);
    if (Vector3.Angle(targetDirection, Quaternion.AngleAxis(90, Vector3.up) * playerDirection) < 90f)
    {
      angle *= -1;
    }


    root.rotation = Quaternion.Euler(new Vector3(playerSpeed * 4, Mathf.Atan2(smoothedVelocity.x, smoothedVelocity.z) * Mathf.Rad2Deg, Mathf.Clamp(angle, -35, 35f)));
    chest.localRotation = Quaternion.Euler(0, animationSin * Mathf.Clamp(playerSpeed, 0, 3) * 4, 0);

    Debug.Log(root.rotation.eulerAngles.y);
    Debug.DrawLine(root.position, targetDirection + root.position, Color.red);
    Debug.DrawLine(root.position, playerDirection + root.position, Color.green);
    Debug.DrawLine(root.position, Quaternion.AngleAxis(90, Vector3.up) * playerDirection + root.position, Color.blue);

  }

  void animateJump()
  {

  }
}
