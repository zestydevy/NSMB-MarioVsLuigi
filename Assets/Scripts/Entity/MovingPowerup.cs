﻿using UnityEngine;
using Photon.Pun;

public class MovingPowerup : MonoBehaviourPun {

    private static int groundMask = -1;
    public float speed, bouncePower, terminalVelocity = 4, blinkingRate = 4;
    private Rigidbody2D body;
    private SpriteRenderer sRenderer;
    private bool right = true;
    public bool passthrough, avoidPlayers;
    public PlayerController followMe;
    public float followMeCounter, despawnCounter = 15, ignoreCounter;
    private PhysicsEntity physics;
    private Animator childAnimator;
    private BoxCollider2D hitbox;
    private int originalLayer;

    public Powerup powerupScriptable;

    void Start() {
        body = GetComponent<Rigidbody2D>();
        sRenderer = GetComponentInChildren<SpriteRenderer>();
        physics = GetComponent<PhysicsEntity>();
        childAnimator = GetComponentInChildren<Animator>();
        hitbox = GetComponent<BoxCollider2D>();

        originalLayer = sRenderer.sortingOrder;

        object[] data = photonView.InstantiationData;
        if (data != null) {
            if (data[0] is float ignore) {
                ignoreCounter = ignore;
                gameObject.layer = LayerMask.NameToLayer("Entity");
            } else if (data[0] is int follow) {
                followMe = PhotonView.Find(follow).GetComponent<PlayerController>();
                followMeCounter = 1f;
                passthrough = true;
                body.isKinematic = true;
                gameObject.layer = LayerMask.NameToLayer("HitsNothing");
                sRenderer.sortingOrder = 15;
                transform.position = new(transform.position.x, transform.position.y, -5);
            } else {
                passthrough = false;
            }
        } else {
            passthrough = false;
            gameObject.layer = LayerMask.NameToLayer("Entity");
        }

        if (groundMask == -1)
            groundMask = LayerMask.GetMask("Ground", "PassthroughInvalid");
    }

    void LateUpdate() {
        ignoreCounter -= Time.deltaTime;
        if (!followMe)
            return;

        //Following someone.
        float size = followMe.flying ? 3.8f : 2.8f;
        transform.position = new Vector3(followMe.transform.position.x, followMe.cameraController.currentPosition.y + (size*0.6f));

        sRenderer.enabled = followMeCounter * blinkingRate % 2 > 1;
        if ((followMeCounter -= Time.deltaTime) < 0) {
            followMe = null;
            sRenderer.sortingOrder = originalLayer;
            if (photonView.IsMine) {
                photonView.TransferOwnership(PhotonNetwork.MasterClient);
                passthrough = true;
            }
        }
    }

    void FixedUpdate() {
        if (GameManager.Instance && GameManager.Instance.gameover) {
            body.velocity = Vector2.zero;
            body.isKinematic = true;
            return;
        }
        if (followMe)
            return;

        despawnCounter -= Time.fixedDeltaTime;
        sRenderer.enabled = !(despawnCounter <= 3 && despawnCounter * blinkingRate % 1 < 0.5f);

        if (despawnCounter <= 0 && photonView.IsMine) {
            photonView.RPC("DespawnWithPoof", RpcTarget.All);
            return;
        }

        body.isKinematic = false;
        if (passthrough) {
            Vector2 origin = body.position + Vector2.up * hitbox.size * transform.lossyScale * 0.5f;
            if (!Utils.IsTileSolidAtWorldLocation(origin) && !Physics2D.OverlapBox(origin, hitbox.size * transform.lossyScale, 0, groundMask)) {
                gameObject.layer = LayerMask.NameToLayer("Entity");
                passthrough = false;
            } else {
                return;
            }
        } else {
            HandleCollision();
        }

        if (physics.onGround && !passthrough && childAnimator) {
            childAnimator.SetTrigger("trigger");
            hitbox.enabled = false;
            body.isKinematic = true;
            body.gravityScale = 0;
        }
        if (avoidPlayers && physics.onGround && !followMe) {
            Collider2D closest = null;
            Vector2 closestPosition = Vector2.zero;
            float distance = float.MaxValue;
            foreach (var hit in Physics2D.OverlapCircleAll(body.position, 10f)) {
                if (!hit.CompareTag("Player"))
                    continue;
                Vector2 actualPosition = hit.attachedRigidbody.position + hit.offset;
                float tempDistance = Vector2.Distance(actualPosition, body.position);
                if (tempDistance > distance)
                    continue;
                distance = tempDistance;
                closest = hit;
                closestPosition = actualPosition;
            }
            if (closest)
                right = (closestPosition.x - body.position.x) < 0;
        }

        body.velocity = new Vector2(body.velocity.x, Mathf.Max(-terminalVelocity, body.velocity.y));
    }
    void HandleCollision() {
        physics.UpdateCollisions();
        if (physics.hitLeft || physics.hitRight) {
            right = physics.hitLeft;
            body.velocity = new Vector2(speed * (right ? 1 : -1), body.velocity.y);
        }
        if (physics.onGround) {
            body.velocity = new Vector2(speed * (right ? 1 : -1), bouncePower);
            if ((physics.hitRoof || (physics.hitLeft && physics.hitRight)) && photonView.IsMine) {
                photonView.RPC("DespawnWithPoof", RpcTarget.All);
                return;
            }
        }
    }

    [PunRPC]
    public void DespawnWithPoof() {
        Instantiate(Resources.Load("Prefabs/Particle/Puff"), transform.GetChild(0).position, Quaternion.identity);
        if (photonView.IsMine)
            PhotonNetwork.Destroy(gameObject);
        Destroy(gameObject);
    }
}
