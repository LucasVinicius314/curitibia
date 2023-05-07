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
  Vector2 c;
  Vector2 smoothedVelocity;
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
}
