using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using Photon.Pun;
using ExitGames.Client.Photon;

using NSMB.Player.State;

public class PlayerController : MonoBehaviourPun, IPunObservable {

    public static int ANY_GROUND_MASK = -1, ONLY_GROUND_MASK, GROUND_LAYERID, HITS_NOTHING_LAYERID, DEFAULT_LAYERID;

    public int playerId = -1;
    public bool dead = false;

    private Enums.PowerupState _state = Enums.PowerupState.Small;
    public Enums.PowerupState State {
        get {
            return _state;
        }
        set {
            if (value != _state) {
                CurrentState?.OnPowerdown(this);
                _state = value;
                CurrentState?.OnPowerup(this);
            }
        }
    }
    public Enums.PowerupState previousState;
    public float slowriseGravity = 0.85f, normalGravity = 2.5f, flyingGravity = 0.8f, flyingTerminalVelocity = 1.25f, drillVelocity = 7f, groundpoundTime = 0.25f, groundpoundVelocity = 10, blinkingSpeed = 0.25f, terminalVelocity = -7f, jumpVelocity = 6.25f, megaJumpVelocity = 16f, spinnerLaunchVelocity = 12f, walkingAcceleration = 8f, runningAcceleration = 3f, walkingMaxSpeed = 2.7f, runningMaxSpeed = 5, wallslideSpeed = -4.25f, walljumpVelocity = 5.6f, giantStartTime = 1.5f, soundRange = 10f, slopeSlidingAngle = 12.5f;


    private readonly IPowerupState[] powerupStateInstances = { null, new MiniPowerupState(), new DefaultPowerupState(), new DefaultPowerupState(), new FireFlowerPowerupState(), new IceFlowerPowerupState(), new PropellerPowerupState(), null, null, null };
    IPowerupState CurrentState {
        get {
            return powerupStateInstances[(int) State];
        }
    }

    public BoxCollider2D[] hitboxes;
    GameObject models;

    public CameraController cameraController;
    public FadeOutManager fadeOut;

    private AudioSource sfx;
    public Animator animator;
    public Rigidbody2D body;
    private PlayerAnimationController animationController;

    public bool onGround, crushGround, doGroundSnap, jumping, properJump, hitRoof, skidding, turnaround, facingRight = true, singlejump, doublejump, triplejump, bounce, crouching, groundpound, sliding, knockback, hitBlock, functionallyRunning, flying, drill, hitLeft, hitRight, iceSliding, stuckInBlock, frozen, stationaryGiantEnd, fireballKnockback, startedSliding;
    public float landing, koyoteTime, groundpoundCounter, groundpoundDelay, hitInvincibilityCounter, powerupFlash, throwInvincibility, jumpBuffer, giantStartTimer, giantEndTimer, frozenStruggle;
    public float invincible, giantTimer, floorAngle, knockbackTimer, unfreezeTimer;

    public PlayerInputs inputs = new();

    //Walljumping variables
    public float wallSlideTimer, wallJumpTimer;
    public bool wallSlideLeft, wallSlideRight;


    public Vector2 pipeDirection;
    public int stars, coins, lives = -1;
    public Powerup storedPowerup = null;
    public HoldableEntity holding, holdingOld;
    public FrozenCube FrozenObject;

    private bool powerupButtonHeld;
    private readonly float analogDeadzone = 0.35f;
    public Vector2 joystick, giantSavedVelocity, previousFrameVelocity, previousFramePosition;

    public GameObject onSpinner;
    public PipeManager pipeEntering;
    private bool step, alreadyGroundpounded;
    private int starDirection;
    public PlayerData character;

    //Tile data
    private Enums.Sounds footstepSound = Enums.Sounds.Player_Walk_Grass;
    public bool doIceSkidding;
    private float tileFriction = 1;
    public readonly HashSet<Vector3Int> tilesStandingOn = new(),
        tilesJumpedInto = new(),
        tilesHitSide = new();

    private GameObject trackIcon;

    private ExitGames.Client.Photon.Hashtable gameState = new() {
        [Enums.NetPlayerProperties.GameState] = new ExitGames.Client.Photon.Hashtable()
    }; //only used to update spectating players

    private bool initialKnockbackFacingRight = false;

    #region -- SERIALIZATION / EVENTS --

    private long localFrameId = 0;
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {
            stream.SendNext(body.position);
            stream.SendNext(body.velocity);
            stream.SendNext(inputs);
            stream.SendNext(localFrameId++);

        } else if (stream.IsReading) {
            Vector2 pos = (Vector2) stream.ReceiveNext();
            Vector2 vel = (Vector2) stream.ReceiveNext();
            PlayerInputs controls = (PlayerInputs) stream.ReceiveNext();
            long frameId = (long) stream.ReceiveNext();

            if (frameId < localFrameId)
                //recevied info older than what we have
                return;

            if (GameManager.Instance && GameManager.Instance.gameover)
                //we're DONE with you
                return;

            float lag = (float) (PhotonNetwork.Time - info.SentServerTime);

            body.position = pos;
            body.velocity = vel;
            inputs = controls;
            localFrameId = frameId;

            int fullResims = (int) (lag / Time.fixedDeltaTime);
            float partialResim = lag % Time.fixedDeltaTime;

            while (fullResims-- > 0)
                HandleMovement(Time.fixedDeltaTime);
            HandleMovement(partialResim);
        }
    }
    #endregion

    #region -- START / UPDATE --
    public void Awake() {
        //todo: move to layers constant?
        if (ANY_GROUND_MASK == -1) {
            ANY_GROUND_MASK = LayerMask.GetMask("Ground", "Semisolids");
            ONLY_GROUND_MASK = LayerMask.GetMask("Ground");
            GROUND_LAYERID = LayerMask.NameToLayer("Ground");
            HITS_NOTHING_LAYERID = LayerMask.NameToLayer("HitsNothing");
            DEFAULT_LAYERID = LayerMask.NameToLayer("Default");
        }

        cameraController = GetComponent<CameraController>();
        cameraController.controlCamera = photonView.IsMine;

        animator = GetComponentInChildren<Animator>();
        body = GetComponent<Rigidbody2D>();
        sfx = GetComponent<AudioSource>();
        animationController = GetComponent<PlayerAnimationController>();
        fadeOut = GameObject.FindGameObjectWithTag("FadeUI").GetComponent<FadeOutManager>();

        models = transform.Find("Models").gameObject;
        starDirection = Random.Range(0, 4);

        playerId = PhotonNetwork.CurrentRoom != null ? System.Array.IndexOf(PhotonNetwork.PlayerList, photonView.Owner) : -1;
        Utils.GetCustomProperty(Enums.NetRoomProperties.Lives, out lives);
        if (lives != -1)
            lives++;

        InputSystem.controls.Player.Movement.performed += OnMovement;
        InputSystem.controls.Player.Jump.performed += OnJump;
        InputSystem.controls.Player.Sprint.started += OnSprint;
        InputSystem.controls.Player.Sprint.canceled += OnSprint;
        InputSystem.controls.Player.PowerupAction.performed += OnPowerupAction;
        InputSystem.controls.Player.ReserveItem.performed += OnReserveItem;
    }


    public void Start() {
        hitboxes = GetComponents<BoxCollider2D>();
        trackIcon = UIUpdater.Instance.CreatePlayerIcon(this);
        transform.position = body.position = GameManager.Instance.spawnpoint;
        cameraController.Recenter();

        LoadFromGameState();
    }
    
    public void OnDestroy() {
        if (!photonView.IsMine)
            return;

        InputSystem.controls.Player.Movement.performed -= OnMovement;
        InputSystem.controls.Player.Jump.performed -= OnJump;
        InputSystem.controls.Player.Sprint.started -= OnSprint;
        InputSystem.controls.Player.Sprint.canceled -= OnSprint;
        InputSystem.controls.Player.PowerupAction.performed -= OnPowerupAction;
        InputSystem.controls.Player.ReserveItem.performed -= OnReserveItem;
    }

    public void OnGameStart() {
        gameObject.SetActive(true);
        photonView.RPC("PreRespawn", RpcTarget.All);

        gameState = new() {
            [Enums.NetPlayerProperties.GameState] = new ExitGames.Client.Photon.Hashtable()
        };
    }
    
    public void LoadFromGameState() {
        if (photonView.Owner.CustomProperties[Enums.NetPlayerProperties.GameState] is not ExitGames.Client.Photon.Hashtable gs)
            return;

        lives = (int) gs[Enums.NetPlayerGameState.Lives];
        stars = (int) gs[Enums.NetPlayerGameState.Stars];
        coins = (int) gs[Enums.NetPlayerGameState.Coins];
        State = (Enums.PowerupState) gs[Enums.NetPlayerGameState.PowerupState];
    }

    public void UpdateGameState() {
        UpdateGameStateVariable(Enums.NetPlayerGameState.Lives, lives);
        UpdateGameStateVariable(Enums.NetPlayerGameState.Stars, stars);
        UpdateGameStateVariable(Enums.NetPlayerGameState.Coins, coins);
        UpdateGameStateVariable(Enums.NetPlayerGameState.PowerupState, (int) State);

        PhotonNetwork.LocalPlayer.SetCustomProperties(gameState);
    }

    private void UpdateGameStateVariable(string key, object value) {
        ((ExitGames.Client.Photon.Hashtable) gameState[Enums.NetPlayerProperties.GameState])[key] = value;
    }

    public void LateUpdate() {
        if (frozen) {
            body.velocity = Vector2.zero;
            if (/*inShell ||*/ State == Enums.PowerupState.Small) {
                transform.position = FrozenObject.transform.position + new Vector3(0f, -0.3f, 0);
            } else {
                transform.position = FrozenObject.transform.position + new Vector3(0f, -0.45f, 0);
            }
            return;
        }
    }

    public void FixedUpdate() {
        //game ended, freeze.

        if (!GameManager.Instance.musicEnabled) {
            models.SetActive(false);
            return;
        }
        if (GameManager.Instance.gameover) {
            body.velocity = Vector2.zero;
            animator.enabled = false;
            body.isKinematic = true;
            return;
        }

        if (frozen && !dead) {
            if ((unfreezeTimer -= Time.fixedDeltaTime) < 0) {
                FrozenObject.photonView.RPC("SpecialKill", RpcTarget.All, body.position.x > body.position.x, false);
            }
            return;
        }

        if (!dead) {
            HandleTemporaryInvincibility();
            bool snapped = GroundSnapCheck();
            HandleGroundCollision();
            onGround |= snapped;
            doGroundSnap = onGround;
            HandleTileProperties();
            TickCounters();
            HandleMovement(Time.fixedDeltaTime);
        }
        animationController.UpdateAnimatorStates();
        previousFrameVelocity = body.velocity;
        previousFramePosition = body.position;
    }
    #endregion

    #region -- COLLISIONS --
    void HandleGroundCollision() {
        tilesJumpedInto.Clear();
        tilesStandingOn.Clear();
        tilesHitSide.Clear();

        bool ignoreRoof = false;
        int down = 0, left = 0, right = 0, up = 0;

        crushGround = false;
        foreach (BoxCollider2D hitbox in hitboxes) {
            ContactPoint2D[] contacts = new ContactPoint2D[20];
            int collisionCount = hitbox.GetContacts(contacts);

            for (int i = 0; i < collisionCount; i++) {
                ContactPoint2D contact = contacts[i];
                Vector2 n = contact.normal;
                Vector2 p = contact.point + (contact.normal * -0.15f);
                if (n == Vector2.up && contact.point.y > body.position.y)
                    continue;
                Vector3Int vec = Utils.WorldToTilemapPosition(p);
                if (!contact.collider || contact.collider.CompareTag("Player"))
                    continue;

                if (Vector2.Dot(n, Vector2.up) > .05f) {
                    if (Vector2.Dot(body.velocity.normalized, n) > 0.1f && !onGround) {
                        if (!contact.rigidbody || contact.rigidbody.velocity.y < body.velocity.y)
                            //invalid flooring
                            continue;
                    }
                    crushGround |= !contact.collider.gameObject.CompareTag("platform");
                    down++;
                    tilesStandingOn.Add(vec);
                } else if (contact.collider.gameObject.layer == GROUND_LAYERID) {
                    if (Vector2.Dot(n, Vector2.left) > .9f) {
                        right++;
                        tilesHitSide.Add(vec);
                    } else if (Vector2.Dot(n, Vector2.right) > .9f) {
                        left++;
                        tilesHitSide.Add(vec);
                    } else if (Vector2.Dot(n, Vector2.down) > .9f) {
                        up++;
                        tilesJumpedInto.Add(vec);
                    }
                } else {
                    ignoreRoof = true;
                }
            }
        }

        onGround = down >= 1;
        hitLeft = left >= 1;
        hitRight = right >= 1;
        hitRoof = !ignoreRoof && up >= 2;
    }
    void HandleTileProperties() {
        doIceSkidding = false;
        tileFriction = -1;
        footstepSound = Enums.Sounds.Player_Walk_Grass;
        foreach (Vector3Int pos in tilesStandingOn) {
            TileBase tile = Utils.GetTileAtTileLocation(pos);
            if (tile == null)
                continue;
            if (tile is TileWithProperties propTile) {
                footstepSound = propTile.footstepSound;
                doIceSkidding = propTile.iceSkidding;
                tileFriction = Mathf.Max(tileFriction, propTile.frictionFactor);
            } else {
                tileFriction = 1;
            }
        }
        if (tileFriction == -1)
            tileFriction = 1;
    }

    public void OnCollisionStay2D(Collision2D collision) {
        if (!photonView.IsMine || (knockback && !fireballKnockback) || frozen)
            return;

        switch (collision.gameObject.tag) {
        case "Player": {
            //hit players
            foreach (ContactPoint2D contact in collision.contacts) {
                GameObject otherObj = collision.gameObject;
                PlayerController other = otherObj.GetComponent<PlayerController>();
                PhotonView otherView = other.photonView;

                if (other.animator.GetBool("invincible")) {
                    //They are invincible. let them decide if they've hit us.
                    if (invincible > 0) {
                        //oh, we both are. bonk.
                        photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 1, true, otherView.ViewID);
                        other.photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 1, true, photonView.ViewID);
                    }
                    return;
                }

                if (invincible > 0) {
                    //we are invincible. murder time :)
                    if (other.State == Enums.PowerupState.MegaMushroom) {
                        //wait fuck-
                        photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 1, true, otherView.ViewID);
                        return;
                    }

                    otherView.RPC("Powerdown", RpcTarget.All, false);
                    return;
                }

                bool above = Vector2.Dot((body.position - other.body.position).normalized, Vector2.up) > (inShell ? .1f : .7f);
                bool otherAbove = Vector2.Dot((body.position - other.body.position).normalized, Vector2.up) < -(other.inShell ? .1f : .7f);

                if (State == Enums.PowerupState.MegaMushroom || other.State == Enums.PowerupState.MegaMushroom) {
                    if (State == Enums.PowerupState.MegaMushroom && other.State == Enums.PowerupState.MegaMushroom) {
                        //both giant
                        if (above) {
                            bounce = true;
                        } else if (!otherAbove) {
                            otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 0, true, photonView.ViewID);
                            photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 0, true, otherView.ViewID);
                        }
                    } else if (State == Enums.PowerupState.MegaMushroom) {
                        //only we are giant
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                    }
                    return;
                }


                if (above) {
                    //hit them from above
                    bounce = !groundpound && !drill;

                    if (State == Enums.PowerupState.MiniMushroom && other.State != Enums.PowerupState.MiniMushroom) {
                        //we are mini, they arent. special rules.
                        if (groundpound) {
                            otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, (groundpound || drill) ? 3 : 1, false, photonView.ViewID);
                            groundpound = false;
                            bounce = true;
                        } else {
                            photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Enemy_Generic_Stomp);
                        }
                    } else if (other.State == Enums.PowerupState.MiniMushroom && (groundpound || drill)) {
                        //we are big, groundpounding a mini opponent. squish.
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                        bounce = false;
                    } else {
                        otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, (other.State == Enums.PowerupState.MiniMushroom || groundpound || drill) ? 3 : 1, false, photonView.ViewID);
                    }
                    body.velocity = new Vector2(previousFrameVelocity.x, body.velocity.y);

                    return;
                } else if (inShell && (other.inShell || above)) {
                    if (other.inShell) {
                        otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 1, false, photonView.ViewID);
                        photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 1, false, otherView.ViewID);
                    } else {
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                        body.velocity = new Vector2(-body.velocity.x, body.velocity.y);
                    }
                    return;
                } else if (!otherAbove && onGround && other.onGround && Mathf.Abs(previousFrameVelocity.x) > walkingMaxSpeed - 0.5) {
                    //bump?

                    otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 1, true, photonView.ViewID);
                    photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 1, true, otherView.ViewID);
                }
            }
            break;
        }
        case "MarioBrosPlatform": {
            List<Vector2> points = new();
            foreach (ContactPoint2D c in collision.contacts) {
                if (c.normal != Vector2.down)
                    continue;

                points.Add(c.point);
            }
            if (points.Count == 0)
                return;

            Vector2 avg = new();
            foreach (Vector2 point in points)
                avg += point;
            avg /= points.Count;

            collision.gameObject.GetComponent<MarioBrosPlatform>().Bump(this, avg);
            photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.World_Block_Bump);
            break;
        }
        }
    }

    public void OnTriggerEnter2D(Collider2D collider) {
        if (!photonView.IsMine || dead || frozen)
            return;

        HoldableEntity holdable = collider.GetComponentInParent<HoldableEntity>();
        if (holdable && (holding == holdable || (holdingOld == holdable && throwInvincibility > 0)))
            return;
        KillableEntity killable = collider.GetComponentInParent<KillableEntity>();
        if (killable && !killable.dead && !killable.frozen) {
            killable.InteractWithPlayer(this);
            return;
        }

        GameObject obj = collider.gameObject;
        switch (obj.tag) {
        case "Fireball": {
            FireballMover fireball = obj.GetComponentInParent<FireballMover>();
            if (fireball.photonView.IsMine || hitInvincibilityCounter > 0)
                break;

            fireball.photonView.RPC("Kill", RpcTarget.All);
            if (!fireball.isIceball) {
                if (State == Enums.PowerupState.MegaMushroom && State == Enums.PowerupState.BlueShell && (inShell || crouching || groundpound) || invincible > 0)
                    break;
                if (State == Enums.PowerupState.MiniMushroom) {
                    photonView.RPC("Powerdown", RpcTarget.All, false);
                } else if (State != Enums.PowerupState.MegaMushroom) {
                    photonView.RPC("Knockback", RpcTarget.All, fireball.left, 1, true, fireball.photonView.ViewID);
                }
            } else {

                if (State == Enums.PowerupState.MiniMushroom) {
                    photonView.RPC("Powerdown", RpcTarget.All, false);
                } else if (!frozen && !FrozenObject && State != Enums.PowerupState.MegaMushroom && !pipeEntering && !knockback && hitInvincibilityCounter <= 0) {

                    GameObject frozenBlock = PhotonNetwork.Instantiate("Prefabs/FrozenCube", transform.position + new Vector3(0, 0.1f, 0), Quaternion.identity);
                    frozenBlock.GetComponent<FrozenCube>().photonView.RPC("setFrozenEntity", RpcTarget.All, gameObject.tag, photonView.ViewID);

                }
            }
            break;
        }
        case "lava":
        case "poison": {
            if (!photonView.IsMine)
                return;
            photonView.RPC("Death", RpcTarget.All, false, obj.CompareTag("lava"));
            break;
        }
        }
    }

    protected void OnTriggerStay2D(Collider2D collider) {
        GameObject obj = collider.gameObject;
        if (obj.CompareTag("spinner")) {
            onSpinner = obj;
            return;
        }

        if (!photonView.IsMine || dead || frozen)
            return;

        switch (obj.tag) {
        case "Powerup": {
            if (!photonView.IsMine)
                return;
            MovingPowerup powerup = obj.GetComponentInParent<MovingPowerup>();
            if (powerup.followMeCounter > 0 || powerup.ignoreCounter > 0)
                break;
            photonView.RPC("Powerup", RpcTarget.AllViaServer, powerup.photonView.ViewID);
            Destroy(collider);
            break;
        }
        case "bigstar":
            photonView.RPC("CollectBigStar", RpcTarget.AllViaServer, obj.transform.parent.gameObject.GetPhotonView().ViewID);
            break;
        case "loosecoin":
            Transform parent = obj.transform.parent;
            photonView.RPC("CollectCoin", RpcTarget.AllViaServer, parent.gameObject.GetPhotonView().ViewID, parent.position);
            break;
        case "coin":
            photonView.RPC("CollectCoin", RpcTarget.AllViaServer, obj.GetPhotonView().ViewID, new Vector3(obj.transform.position.x, collider.transform.position.y, 0));
            break;
        }
    }

    protected void OnTriggerExit2D(Collider2D collider) {
        if (collider.CompareTag("spinner"))
            onSpinner = null;
    }
    #endregion

    #region -- CONTROLLER FUNCTIONS --
    public void OnMovement(InputAction.CallbackContext context) {
        if (!photonView.IsMine || GameManager.Instance.paused)
            return;

        joystick = context.ReadValue<Vector2>();
        if (frozen)
            photonView.RPC("FrozenStruggle", RpcTarget.All, true);
    }

    public void OnJump(InputAction.CallbackContext context) {
        if (!photonView.IsMine || GameManager.Instance.paused)
            return;

        inputs.jumpHeld = context.ReadValue<float>() >= 0.5f;
        if (inputs.jumpHeld)
            jumpBuffer = 0.15f;

        if (frozen)
            photonView.RPC("FrozenStruggle", RpcTarget.All, false);
    }

    public void OnSprint(InputAction.CallbackContext context) {
        if (!photonView.IsMine || GameManager.Instance.paused)
            return;

        inputs.sprint = context.started;

        if (frozen) {
            photonView.RPC("FrozenStruggle", RpcTarget.All, false);
            return;
        }

        if (GlobalController.Instance.settings.fireballFromSprint)
            CurrentState?.OnPowerupButton(this, inputs.sprint, true);
    }

    public void OnPowerupAction(InputAction.CallbackContext context) {
        if (!photonView.IsMine || dead || GameManager.Instance.paused)
            return;
        powerupButtonHeld = context.ReadValue<float>() >= 0.5f;

        CurrentState?.OnPowerupButton(this, powerupButtonHeld, false);
    }

    public void OnReserveItem(InputAction.CallbackContext context) {
        if (!photonView.IsMine || storedPowerup == null || GameManager.Instance.paused || dead)
            return;

        SpawnItem(storedPowerup);
        storedPowerup = null;
    }
    #endregion

    #region -- POWERUP / POWERDOWN --
    [PunRPC]
    protected void Powerup(int actor) {
        PhotonView view;
        if (dead || !(view = PhotonView.Find(actor)))
            return;

        Powerup powerup = view.GetComponent<MovingPowerup>().powerupScriptable;
        Enums.PowerupState newState = powerup.state;
        Enums.PriorityPair pp = Enums.PowerupStatePriority[powerup.state];
        Enums.PriorityPair cp = Enums.PowerupStatePriority[State];
        bool reserve = cp.statePriority > pp.itemPriority || State == newState;
        bool soundPlayed = false;

        if (powerup.state == Enums.PowerupState.MegaMushroom && State != Enums.PowerupState.MegaMushroom) {

            giantStartTimer = giantStartTime;
            knockback = false;
            groundpound = false;
            crouching = false;
            flying = false;
            drill = false;
            giantTimer = 15f;
            transform.localScale = Vector3.one;
            Instantiate(Resources.Load("Prefabs/Particle/GiantPowerup"), transform.position, Quaternion.identity);

            PlaySoundEverywhere(powerup.soundEffect);
            soundPlayed = true;

        } else if (powerup.prefab == "Star") {
            //starman
            invincible = 10f;
            PlaySound(powerup.soundEffect);

            if (holding && photonView.IsMine) {
                holding.photonView.RPC("SpecialKill", RpcTarget.All, facingRight, false);
                holding = null;
            }

            if (view.IsMine)
                PhotonNetwork.Destroy(view);
            Destroy(view.gameObject);

            return;
        }

        if (reserve) {
            if (storedPowerup == null || (storedPowerup != null && Enums.PowerupStatePriority[storedPowerup.state].itemPriority <= pp.itemPriority) && !(State == Enums.PowerupState.Large && newState != Enums.PowerupState.Large)) {
                //dont reserve mushrooms 
                storedPowerup = powerup;
            }
            PlaySound(Enums.Sounds.Player_Sound_PowerupReserveStore);
        } else {

            if (!(State == Enums.PowerupState.Large && newState != Enums.PowerupState.Large) && (storedPowerup == null || Enums.PowerupStatePriority[storedPowerup.state].itemPriority <= cp.itemPriority)) {
                storedPowerup = (Powerup) Resources.Load("Scriptables/Powerups/" + State);
            }

            previousState = State;
            State = newState;
            powerupFlash = 2;
            crouching |= ForceCrouchCheck();
            drill &= flying;
            
            if (!soundPlayed)
                PlaySound(powerup.soundEffect);
        }

        UpdateGameState();

        if (view.IsMine)
            PhotonNetwork.Destroy(view);
        Destroy(view.gameObject);
    }

    [PunRPC]
    public void Powerdown(bool ignoreInvincible = false) {
        if (!ignoreInvincible && hitInvincibilityCounter > 0)
            return;

        previousState = State;
        bool nowDead = false;

        switch (State) {
        case Enums.PowerupState.MiniMushroom:
        case Enums.PowerupState.Small: {
            if (photonView.IsMine)
                photonView.RPC("Death", RpcTarget.All, false, false);
            nowDead = true;
            break;
        }
        case Enums.PowerupState.Large: {
            State = Enums.PowerupState.Small;
            powerupFlash = 2f;
            SpawnStars(1, false);
            break;
        }
        case Enums.PowerupState.FireFlower:
        case Enums.PowerupState.IceFlower:
        case Enums.PowerupState.PropellerMushroom:
        case Enums.PowerupState.BlueShell: {
            State = Enums.PowerupState.Large;
            powerupFlash = 2f;
            SpawnStars(1, false);
            break;
        }
        }

        if (!nowDead) {
            hitInvincibilityCounter = 3f;
            PlaySound(Enums.Sounds.Player_Sound_Powerdown);
        }
    }
    #endregion

    // I didn't know what region to put this, move it if needed.

    [PunRPC]
    protected void Freeze() {
        if (knockback || hitInvincibilityCounter > 0 || frozen || State == Enums.PowerupState.BlueShell)
            return;
        if (invincible > 0) {
            Instantiate(Resources.Load("Prefabs/Particle/IceBreak"), transform.position, Quaternion.identity);
            photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Enemy_Generic_FreezeShatter);
            return;
        }
        frozen = true;
        animator.enabled = false;
        body.isKinematic = true;
        body.simulated = false;
    }
    [PunRPC]
    protected void Unfreeze() {
        frozen = false;
        animator.enabled = true;
        body.isKinematic = false;
        body.simulated = true;
        PlaySound(Enums.Sounds.Enemy_Generic_FreezeShatter);
        photonView.RPC("Knockback", RpcTarget.All, false, 1, true, 0);
        frozenStruggle = 0;
        unfreezeTimer = 3;
    }

    [PunRPC]
    protected void FrozenStruggle(bool movement = false) {
        // FrozenStruggle is called by OnJump and that is called everytime jump is pushed or letgo so I just put a 4 there.
        if (unfreezeTimer > 0) {
            unfreezeTimer -= movement ? 0.02f : 0.04f;
            return;
        } else {
            FrozenObject.photonView.RPC("SpecialKill", RpcTarget.All, body.position.x > body.position.x, false);
        }
    }

    #region -- PHOTON SETTERS --
    [PunRPC]
    protected void SetCoins(int coins) {
        this.coins = coins;
    }
    [PunRPC]
    protected void SetStars(int stars) {
        this.stars = stars;
    }
    #endregion

    #region -- COIN / STAR COLLECTION --
    [PunRPC]
    protected void CollectBigStar(int starID) {
        PhotonView view = PhotonView.Find(starID);
        if (view == null)
            return;

        GameObject star = view.gameObject;
        StarBouncer starScript = star.GetComponent<StarBouncer>();
        if (!starScript.IsCollectible())
            return;

        if (photonView.IsMine && starScript.stationary)
            GameManager.Instance.SendAndExecuteEvent(Enums.NetEventIds.ResetTiles, null, SendOptions.SendReliable);

        stars++;
        if (photonView.IsMine)
            photonView.RPC("SetStars", RpcTarget.Others, stars); //just in case
        GameManager.Instance.CheckForWinner();

        Instantiate(Resources.Load("Prefabs/Particle/StarCollect"), star.transform.position, Quaternion.identity);
        PlaySoundEverywhere(photonView.IsMine ? Enums.Sounds.World_Star_Collect_Self : Enums.Sounds.World_Star_Collect_Enemy);

        UpdateGameState();

        if (view.IsMine)
            PhotonNetwork.Destroy(view);
        DestroyImmediate(star);
    }

    [PunRPC]
    protected void CollectCoin(int coinID, Vector3 position) {
        if (coinID != -1) {
            PhotonView coinView = PhotonView.Find(coinID);
            if (!coinView)
                return;
            GameObject coin = coinView.gameObject;
            if (coin.CompareTag("loosecoin")) {
                if (coin.GetPhotonView().IsMine)
                    PhotonNetwork.Destroy(coin);
                Destroy(coin);
            } else {
                SpriteRenderer renderer = coin.GetComponent<SpriteRenderer>();
                if (!renderer.enabled)
                    return;
                renderer.enabled = false;
                coin.GetComponent<BoxCollider2D>().enabled = false;
            }
        }
        Instantiate(Resources.Load("Prefabs/Particle/CoinCollect"), position, Quaternion.identity);

        PlaySound(Enums.Sounds.World_Coin_Collect);
        GameObject num = (GameObject) Instantiate(Resources.Load("Prefabs/Particle/Number"), position, Quaternion.identity);
        num.GetComponentInChildren<NumberParticle>().SetSprite(coins);
        Destroy(num, 1.5f);

        coins++;
        if (coins >= 8) {
            coins = 0;
            if (photonView.IsMine) 
                SpawnItem();
        }

        UpdateGameState();
    }

    public void SpawnItem(Powerup item = null) {
        string prefab = item == null ? Utils.GetRandomItem(stars).prefab : item.prefab;

        PhotonNetwork.Instantiate("Prefabs/Powerup/" + prefab, body.position + new Vector2(0, 5), Quaternion.identity, 0, new object[] { photonView.ViewID });
        photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_PowerupReserveUse);
    }
    public void SpawnItem(string prefab) {
        PhotonNetwork.Instantiate("Prefabs/Powerup/" + prefab, body.position + new Vector2(0, 5), Quaternion.identity, 0, new object[] { photonView.ViewID });
        photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_PowerupReserveUse);
    }

    void SpawnStars(float amount, bool deathplane) {

        bool fastStars = amount > 2;

        Debug.Log("faststars=" + fastStars);
        while (amount > 0) {
            if (!fastStars) {
                if (starDirection == 0)
                    starDirection = 2;
                if (starDirection == 3)
                    starDirection = 1;
            }
            SpawnStar(deathplane);
            amount--;
        }
    }

    void SpawnStar(bool deathplane) {
        if (stars <= 0)
            return;

        stars--;
        if (photonView.IsMine)
            photonView.RPC("SetStars", RpcTarget.Others, stars);

        if (!PhotonNetwork.IsMasterClient)
            return;

        GameObject star = PhotonNetwork.InstantiateRoomObject("Prefabs/BigStar", body.position + (deathplane ? new Vector2(0, transform.localScale.y * hitboxes[0].size.y) : Vector2.zero), Quaternion.identity, 0, new object[] { starDirection, photonView.ViewID, PhotonNetwork.ServerTimestamp + 1000 });
        if (deathplane) {
            star.GetComponent<Rigidbody2D>().velocity += new Vector2(0, 3);
        }
        starDirection = (starDirection + 1) % 4;

        GameManager.Instance.CheckForWinner();
    }
    #endregion

    #region -- DEATH / RESPAWNING --
    [PunRPC]
    protected void Death(bool deathplane, bool fire) {
        if (dead)
            return;
        dead = true;
        onSpinner = null;
        pipeEntering = null;
        unfreezeTimer = 3;
        flying = false;
        drill = false;
        sliding = false;
        crouching = false;
        skidding = false;
        turnaround = false;
        groundpound = false;
        knockback = false;
        wallSlideLeft = false;
        wallSlideRight = false;
        animator.SetBool("knockback", false);
        animator.SetBool("flying", false);
        animator.SetBool("firedeath", fire);
        animator.Play("deadstart", State >= Enums.PowerupState.Large ? 1 : 0);
        PlaySound(Enums.Sounds.Player_Sound_Death);
        SpawnStars(1, deathplane);
        if (holding) {
            holding.photonView.RPC("Throw", RpcTarget.All, !facingRight, true);
            holding = null;
        }
        CurrentState?.OnPowerdown(this);
    }

    [PunRPC]
    public void PreRespawn() {
        if (--lives == 0) {
            GameManager.Instance.CheckForWinner();
            Destroy(trackIcon);
            if (photonView.IsMine) {
                PhotonNetwork.Destroy(photonView);
                GameManager.Instance.SpectationManager.Spectating = true;
            }
            return;
        }
        transform.localScale = Vector2.one;
        transform.position = body.position = GameManager.Instance.GetSpawnpoint(playerId);
        cameraController.Recenter();
        gameObject.layer = DEFAULT_LAYERID;
        previousState = State = Enums.PowerupState.Small;
        animationController.DisableAllModels();
        dead = false;
        animator.SetTrigger("respawn");
        invincible = 0;
        unfreezeTimer = 3;
        giantTimer = 0;
        giantEndTimer = 0;
        giantStartTimer = 0;
        groundpound = false;
        body.isKinematic = false;

        GameObject particle = (GameObject) Instantiate(Resources.Load("Prefabs/Particle/Respawn"), body.position, Quaternion.identity);
        if (photonView.IsMine)
            particle.GetComponent<RespawnParticle>().player = this;

        gameObject.SetActive(false);
    }

    [PunRPC]
    public void Respawn() {
        gameObject.SetActive(true);
        dead = false;
        State = Enums.PowerupState.Small;
        previousState = Enums.PowerupState.Small;
        body.velocity = Vector2.zero;
        wallSlideLeft = false;
        wallSlideRight = false;
        wallSlideTimer = 0;
        wallJumpTimer = 0;
        flying = false;
        crouching = false;
        onGround = false;
        sliding = false;
        koyoteTime = 1f;
        jumpBuffer = 0;
        invincible = 0;
        giantStartTimer = 0;
        giantTimer = 0;
        singlejump = false;
        doublejump = false;
        turnaround = false;
        triplejump = false;
        knockback = false;
        bounce = false;
        skidding = false;
        groundpound = false;
        landing = 0f;
        ResetKnockback();
        Instantiate(Resources.Load("Prefabs/Particle/Puff"), transform.position, Quaternion.identity);
        models.transform.rotation = Quaternion.Euler(0, 180, 0);
    }
    #endregion

    #region -- SOUNDS / PARTICLES --
    [PunRPC]
    public void PlaySoundEverywhere(Enums.Sounds sound) {
        GameManager.Instance.sfx.PlayOneShot(sound.GetClip(character));
    }
    [PunRPC]
    public void PlaySound(Enums.Sounds sound, byte variant, float volume) {
        sfx.PlayOneShot(sound.GetClip(character, variant), Mathf.Clamp01(volume));
    }
    [PunRPC]
    public void PlaySound(Enums.Sounds sound, byte variant) {
        sfx.PlayOneShot(sound.GetClip(character, variant), 1);
    }
    [PunRPC]
    public void PlaySound(Enums.Sounds sound) {
        PlaySound(sound, 0, 1);
    }

    [PunRPC]
    protected void SpawnParticle(string particle, Vector2 worldPos) {
        Instantiate(Resources.Load(particle), worldPos, Quaternion.identity);
    }

    [PunRPC]
    protected void SpawnParticle(string particle, Vector2 worldPos, Vector3 rot) {
        Instantiate(Resources.Load(particle), worldPos, Quaternion.Euler(rot));
    }
    protected void GiantFootstep() {
        cameraController.screenShakeTimer = 0.15f;
        SpawnParticle("Prefabs/Particle/GroundpoundDust", body.position + new Vector2(facingRight ? 0.5f : -0.5f, 0));
        PlaySound(Enums.Sounds.Powerup_MegaMushroom_Walk, (byte) (step ? 1 : 2));
        step = !step;
    }
    protected void Footstep(int layer) {
        if ((State <= Enums.PowerupState.Small ? 0 : 1) != layer)
            return;
        if (State == Enums.PowerupState.MegaMushroom)
            return;
        if (doIceSkidding && skidding) {
            PlaySound(Enums.Sounds.World_Ice_Skidding);
            return;
        }
        //TODO: change to use proper abstraction
        if (CurrentState is PropellerPowerupState) {
            PlaySound(Enums.Sounds.Powerup_PropellerMushroom_Kick);
            return;
        }
        if (Mathf.Abs(body.velocity.x) < walkingMaxSpeed)
            return;

        PlaySound(footstepSound, (byte) (step ? 1 : 2), Mathf.Abs(body.velocity.x) / (runningMaxSpeed + 4));
        step = !step;
    }
    #endregion

    #region -- TILE COLLISIONS --
    void HandleGiantTiles(bool pipes) {
        if (!photonView.IsMine)
            return;

        Vector2 checkSize = hitboxes[0].size * transform.lossyScale * 1.1f;
        Vector2 normalizedVelocity = body.velocity;
        if (!groundpound)
            normalizedVelocity.y = Mathf.Max(0, body.velocity.y);
        
        Vector2 offset = Vector2.zero;
        if (singlejump && onGround)
            offset = Vector2.down / 2f;

        Vector2 checkPosition = body.position + (5 * Time.fixedDeltaTime * normalizedVelocity) + new Vector2(0, checkSize.y / 2) + offset;

        Vector3Int minPos = Utils.WorldToTilemapPosition(checkPosition - (checkSize / 2), wrap: false);
        Vector3Int size = Utils.WorldToTilemapPosition(checkPosition + (checkSize / 2), wrap: false) - minPos;


        for (int x = 0; x <= size.x; x++) {
            for (int y = 0; y <= size.y; y++) {
                Vector3Int tileLocation = new(minPos.x + x, minPos.y + y, 0);
                Utils.WrapTileLocation(ref tileLocation);

                InteractableTile.InteractionDirection dir = InteractableTile.InteractionDirection.Up;
                if (y == 0 && singlejump && onGround) {
                    dir = InteractableTile.InteractionDirection.Down;
                } else if (tileLocation.x / 2f < checkPosition.x && y != size.y) {
                    dir = InteractableTile.InteractionDirection.Left;
                } else if (tileLocation.x / 2f > checkPosition.x && y != size.y) {
                    dir = InteractableTile.InteractionDirection.Right;
                }

                BreakablePipeTile pipe = GameManager.Instance.tilemap.GetTile<BreakablePipeTile>(tileLocation);
                if (pipe && (pipe.upsideDownPipe || !pipes || groundpound))
                    continue;

                InteractWithTile(tileLocation, dir);
            }
        }
        if (pipes)
            for (int x = 0; x <= size.x; x++) {
                for (int y = size.y; y >= 0; y--) {
                    Vector3Int tileLocation = new(minPos.x + x, minPos.y + y, 0);
                    Utils.WrapTileLocation(ref tileLocation);

                    InteractableTile.InteractionDirection dir = InteractableTile.InteractionDirection.Up;
                    if (y == 0 && singlejump && onGround) {
                        dir = InteractableTile.InteractionDirection.Down;
                    } else if (tileLocation.x / 2f < checkPosition.x && y != size.y) {
                        dir = InteractableTile.InteractionDirection.Left;
                    } else if (tileLocation.x / 2f > checkPosition.x && y != size.y) {
                        dir = InteractableTile.InteractionDirection.Right;
                    }

                    BreakablePipeTile pipe = GameManager.Instance.tilemap.GetTile<BreakablePipeTile>(tileLocation);
                    if (!pipe || !pipe.upsideDownPipe || dir == InteractableTile.InteractionDirection.Up)
                        continue;

                    InteractWithTile(tileLocation, dir);
                }
            }
    }

    public int InteractWithTile(Vector3Int tilePos, InteractableTile.InteractionDirection direction) {
        if (!photonView.IsMine)
            return 0;

        TileBase tile = GameManager.Instance.tilemap.GetTile(tilePos);
        if (!tile)
            return 0;
        if (tile is InteractableTile it)
            return it.Interact(this, direction, Utils.TilemapToWorldPosition(tilePos)) ? 1 : 0;

        return 0;
    }
    #endregion

    #region -- KNOCKBACK --

    [PunRPC]
    protected void Knockback(bool fromRight, int starsToDrop, bool fireball, int attackerView) {
        if (invincible > 0 || (knockback && !fireballKnockback) || hitInvincibilityCounter > 0 || pipeEntering)
            return;

        if (!(CurrentState?.OnKnockback(this, fromRight, starsToDrop, fireball, attackerView) ?? true))
            return;

        knockback = true;
        knockbackTimer = 0.5f;
        fireballKnockback = fireball;
        initialKnockbackFacingRight = facingRight;

        PhotonView attacker = PhotonNetwork.GetPhotonView(attackerView);
        if (attacker)
            SpawnParticle("Prefabs/Particle/PlayerBounce", attacker.transform.position);

        animator.SetBool("fireballKnockback", fireball);
        animator.SetBool("knockforwards", facingRight != fromRight);

        body.velocity = new Vector2((fromRight ? -1 : 1) * 3 * (starsToDrop + 1) * (State == Enums.PowerupState.MegaMushroom ? 3 : 1) * (fireball ? 0.7f : 1f), fireball ? 0 : 4);
        if (onGround && !fireball)
            body.position += Vector2.up * 0.15f;

        onGround = false;
        doGroundSnap = false;
        groundpound = false;
        flying = false;
        sliding = false;
        drill = false;
        body.gravityScale = normalGravity;

        SpawnStars(starsToDrop, false);
    }

    public void ResetKnockbackFromAnim() {
        if (photonView.IsMine)
            photonView.RPC("ResetKnockback", RpcTarget.All);
    }

    [PunRPC]
    protected void ResetKnockback() {
        hitInvincibilityCounter = 2f;
        bounce = false;
        knockback = false;
        body.velocity = new(0, body.velocity.y);
        facingRight = initialKnockbackFacingRight;
    }
    #endregion

    #region -- ENTITY HOLDING --
    [PunRPC]
    protected void HoldingWakeup() {
        holding = null;
        holdingOld = null;
        throwInvincibility = 0;
        Powerdown(false);
    }
    [PunRPC]
    public void SetHolding(int view) {
        if (view == -1) {
            holding = null;
            return;
        }
        holding = PhotonView.Find(view).GetComponent<HoldableEntity>();
    }
    [PunRPC]
    public void SetHoldingOld(int view) {
        if (view == -1) {
            holding = null;
            return;
        }
        holdingOld = PhotonView.Find(view).GetComponent<HoldableEntity>();
        throwInvincibility = 0.5f;
    }
    #endregion

    void HandleSliding() {
        startedSliding = false;
        if (groundpound) {
            if (onGround) {
                if (State == Enums.PowerupState.MegaMushroom) {
                    groundpound = false;
                    groundpoundCounter = 0.5f;
                    return;
                }
                if (!inShell && Mathf.Abs(floorAngle) >= slopeSlidingAngle) {
                    groundpound = false;
                    sliding = true;
                    alreadyGroundpounded = true;
                    body.velocity = new Vector2(-Mathf.Sign(floorAngle) * groundpoundVelocity, 0);
                    startedSliding = true;
                } else {
                    alreadyGroundpounded = false;
                    body.velocity = Vector2.zero;
                    if (!inputs.down || State == Enums.PowerupState.MegaMushroom) {
                        groundpound = false;
                        groundpoundCounter = State == Enums.PowerupState.MegaMushroom ? 0.4f : 0.25f;
                    }
                }
            }
            if (inputs.up && State != Enums.PowerupState.MegaMushroom)
                groundpound = false;
        }
        if (crouching && Mathf.Abs(floorAngle) >= slopeSlidingAngle && !inShell && State != Enums.PowerupState.MegaMushroom) {
            sliding = true;
            crouching = false;
            alreadyGroundpounded = true;
        }
        if (sliding && onGround && Mathf.Abs(floorAngle) > slopeSlidingAngle) {
            float angleDeg = floorAngle * Mathf.Deg2Rad;

            bool uphill = Mathf.Sign(floorAngle) == Mathf.Sign(body.velocity.x);
            float speed = Time.fixedDeltaTime * 5f * (uphill ? Mathf.Clamp01(1f - (Mathf.Abs(body.velocity.x) / runningMaxSpeed)) : 4f);

            float newX = Mathf.Clamp(body.velocity.x - (Mathf.Sin(angleDeg) * speed), -(runningMaxSpeed * 1.3f), runningMaxSpeed * 1.3f);
            float newY = Mathf.Sin(angleDeg) * newX + 0.4f;
            body.velocity = new Vector2(newX, newY);
        }

        if (inputs.up || (Mathf.Abs(floorAngle) < slopeSlidingAngle && onGround && Mathf.Abs(body.velocity.x) < 0.1)) {
            sliding = false;
            alreadyGroundpounded = false;
        }
    }

    void HandleSlopes() {
        if (!onGround) {
            floorAngle = 0;
            return;
        }
        BoxCollider2D mainCollider = hitboxes[0];
        RaycastHit2D hit = Physics2D.BoxCast(body.position + (Vector2.up * 0.05f), new Vector2(mainCollider.size.x * transform.lossyScale.x + (Physics2D.defaultContactOffset * 2f), 0.1f), 0, body.velocity.normalized, (body.velocity * Time.fixedDeltaTime).magnitude, ANY_GROUND_MASK);
        if (hit) {
            //hit ground
            float angle = Vector2.SignedAngle(Vector2.up, hit.normal);
            if (angle < -89 || angle > 89)
                return;

            float x = floorAngle != angle ? previousFrameVelocity.x : body.velocity.x;
            floorAngle = angle;

            float change = Mathf.Sin(angle * Mathf.Deg2Rad) * x * 1.25f;
            body.velocity = new Vector2(x, change);
            onGround = true;
            doGroundSnap = true;
        } else {
            hit = Physics2D.BoxCast(body.position + (Vector2.up * 0.05f), new Vector2(mainCollider.size.x * transform.lossyScale.x + (Physics2D.defaultContactOffset * 2f), 0.1f), 0, Vector2.down, 0.3f, ANY_GROUND_MASK);
            if (hit) {
                float angle = Vector2.SignedAngle(Vector2.up, hit.normal);
                if (angle < -89 || angle > 89)
                    return;

                float x = floorAngle != angle ? previousFrameVelocity.x : body.velocity.x;
                floorAngle = angle;

                float change = Mathf.Sin(angle * Mathf.Deg2Rad) * x * 1.25f;
                body.velocity = new Vector2(x, change);
                onGround = true;
                doGroundSnap = true;
            } else {
                floorAngle = 0;
            }
        }
        if (!(inputs.left || inputs.right) && !inShell && !sliding && Mathf.Abs(floorAngle) > slopeSlidingAngle * 2 && State != Enums.PowerupState.MegaMushroom) {
            //steep slope, continously walk downwards
            float autowalkMaxSpeed = floorAngle / 30;
            if (Mathf.Abs(body.velocity.x) > autowalkMaxSpeed)
                return;

            float newX = Mathf.Clamp(body.velocity.x - (autowalkMaxSpeed * Time.fixedDeltaTime), -Mathf.Abs(autowalkMaxSpeed), Mathf.Abs(autowalkMaxSpeed));
            body.velocity = new Vector2(newX, Mathf.Sin(floorAngle * Mathf.Deg2Rad) * newX);
        }
    }

    bool colliding = true;
    void HandleTemporaryInvincibility() {
        bool shouldntCollide = (hitInvincibilityCounter > 0) || (knockback && !fireballKnockback);
        if (shouldntCollide && colliding) {
            colliding = false;
            foreach (var player in GameManager.Instance.allPlayers) {
                foreach (BoxCollider2D hitbox in hitboxes) {
                    foreach (BoxCollider2D otherHitbox in player.hitboxes)
                        Physics2D.IgnoreCollision(hitbox, otherHitbox, true);
                }
            }
        } else if (!shouldntCollide && !colliding) {
            colliding = true;
            foreach (var player in GameManager.Instance.allPlayers) {
                foreach (BoxCollider2D hitbox in hitboxes) {
                    foreach (BoxCollider2D otherHitbox in player.hitboxes)
                        Physics2D.IgnoreCollision(hitbox, otherHitbox, false);
                }
            }
        }
    }

    bool GroundSnapCheck() {
        if ((body.velocity.y > 0 && !onGround) || !doGroundSnap || pipeEntering)
            return false;

        BoxCollider2D hitbox = hitboxes[0];
        RaycastHit2D hit = Physics2D.BoxCast(body.position + new Vector2(0, 0.1f), new Vector2(hitbox.size.x * transform.lossyScale.x, 0.05f), 0, Vector2.down, 0.4f, ANY_GROUND_MASK);
        if (hit) {
            body.position = new Vector2(body.position.x, hit.point.y + Physics2D.defaultContactOffset);
            return true;
        }
        return false;
    }

    #region -- PIPES --

    void DownwardsPipeCheck() {
        if (!photonView.IsMine || !inputs.down || !onGround || knockback)
            return;

        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 0.5f)) {
            GameObject obj = hit.transform.gameObject;
            if (!obj.CompareTag("pipe"))
                continue;
            PipeManager pipe = obj.GetComponent<PipeManager>();
            AttemptPipeEnter(pipe);
            break;
        }
    }

    void UpwardsPipeCheck() {
        if (!photonView.IsMine || !hitRoof || !inputs.up || knockback)
            return;

        //todo: change to nonalloc?
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.up, 0.5f)) {
            GameObject obj = hit.transform.gameObject;
            if (!obj.CompareTag("pipe"))
                continue;
            PipeManager pipe = obj.GetComponent<PipeManager>();
            AttemptPipeEnter(pipe);
            break;
        }
    }

    void AttemptPipeEnter(PipeManager pipe) {

        //check if mom says its ok :)
        if (!(CurrentState?.OnPipeEnter(this, pipe) ?? true))
            return;

        pipeEntering = pipe;
        body.velocity = pipeDirection = pipe.bottom ? Vector2.up : Vector2.down;
        transform.position = body.position = new(pipe.transform.position.x, body.position.y);
        photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_Powerdown);
        
        crouching = false;
        sliding = false;
        flying = false;
        drill = false;
    }

    #endregion

    void HandleCrouching() {
        if (!photonView.IsMine || sliding || knockback)
            return;
        if (State == Enums.PowerupState.MegaMushroom) {
            crouching = false;
            return;
        }

        bool? toCrouch = null;
        if (crouching) {
            if (onGround && inputs.up && !ForceCrouchCheck()) {
                //release 
                toCrouch = false;
            }
        } else {
            if (onGround && !holding && inputs.down) {
                //crouch
                toCrouch = true;
            }
        }

        if (toCrouch == null)
            return;




        if (!(CurrentState?.OnCrouch(this, toCrouch == true) ?? true))
            return;

        crouching = toCrouch == true;
        if (crouching && toCrouch == true) {
            photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_Crouch);
        }
    }

    bool ForceCrouchCheck() {
        if (State < Enums.PowerupState.Large)
            return false;
        float width = hitboxes[0].bounds.extents.x;

        bool triggerState = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;

        bool ret = Physics2D.BoxCast(body.position + new Vector2(0, 0.86f / 2f + 0.05f), new Vector2(width, 0.71f), 0, Vector2.zero, 0, ONLY_GROUND_MASK);

        Physics2D.queriesHitTriggers = triggerState;
        return ret;
    }

    void HandleWallslide() {

        Vector2 currentWallDirection;
        if (inputs.left) {
            currentWallDirection = Vector2.left;
        } else if (inputs.right) {
            currentWallDirection = Vector2.right;
        } else if (wallSlideLeft) {
            currentWallDirection = Vector2.left;
        } else if (wallSlideRight) {
            currentWallDirection = Vector2.right;
        } else {
            return;
        }

        HandleWallSlideChecks(currentWallDirection);

        wallSlideRight &= wallSlideTimer > 0 && hitRight;
        wallSlideLeft &= wallSlideTimer > 0 && hitLeft;

        if (wallSlideLeft || wallSlideRight) {
            //walljump check
            if (inputs.jumpPressed && wallJumpTimer <= 0) {
                //perform walljump

                hitRight = false;
                hitLeft = false;
                body.velocity = new Vector2(runningMaxSpeed * (3 / 4f) * (wallSlideLeft ? 1 : -1), walljumpVelocity);
                facingRight = wallSlideLeft;
                singlejump = false;
                doublejump = false;
                triplejump = false;
                onGround = false;
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_WallJump);
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Voice_WallJump, (byte) Random.Range(1, 3));

                Vector2 offset = new(hitboxes[0].size.x / 2f * (wallSlideLeft ? -1 : 1), hitboxes[0].size.y / 2f);
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/WalljumpParticle", body.position + offset, wallSlideLeft ? Vector3.zero : Vector3.up * 180);
                
                wallJumpTimer = 16 / 60f;
                wallSlideTimer = 0;
            }
        } else {
            //walljump starting check
            bool canWallslide = body.velocity.y < -0.1 && !groundpound && !onGround && !holding && State != Enums.PowerupState.MegaMushroom && !flying && !drill && !crouching && !sliding;
            if (!canWallslide)
                return;

            //Check 1
            if (wallJumpTimer > 0)
                return;

            //Check 2
            if (wallSlideTimer - Time.fixedDeltaTime <= 0)
                return;

            //Check 4: already handled
            //Check 5.2: already handled

            //Check 6
            if (crouching)
                return;

            //Check 8
            if (!((currentWallDirection == Vector2.right && facingRight) || (currentWallDirection == Vector2.left && !facingRight)))
                return;

            //Check with mom
            if (!(CurrentState?.OnWallslideStart(this, currentWallDirection == Vector2.right) ?? true))
                return;

            //Start wallslide
            wallSlideRight = currentWallDirection == Vector2.right;
            wallSlideLeft = currentWallDirection == Vector2.left;
        }

        wallSlideRight &= wallSlideTimer > 0 && hitRight;
        wallSlideLeft &= wallSlideTimer > 0 && hitLeft;
    }

    void HandleWallSlideChecks(Vector2 wallDirection) {
        bool floorCheck = !Physics2D.Raycast(body.position, Vector2.down, 0.5f, ANY_GROUND_MASK);
        if (!floorCheck) {
            wallSlideTimer = 0;
            return;
        }

        bool moveDownCheck = body.velocity.y < 0;
        if (!moveDownCheck)
            return;

        bool wallCollisionCheck = wallDirection == Vector2.left ? hitLeft : hitRight;
        if (!wallCollisionCheck)
            return;

        bool heightUpperCheck = Physics2D.Raycast(body.position + new Vector2(0, .6f), wallDirection, hitboxes[0].size.x * 2, ONLY_GROUND_MASK);
        if (!heightUpperCheck)
            return;

        bool heightLowerCheck = Physics2D.Raycast(body.position + new Vector2(0, 0), wallDirection, hitboxes[0].size.x * 2, ONLY_GROUND_MASK);
        if (!heightLowerCheck)
            return;

        if ((wallDirection == Vector2.left && !inputs.left) || (wallDirection == Vector2.right && !inputs.right))
            return;

        wallSlideTimer = 16 / 60f;
    }

    void HandleJumping(bool jump) {
        if (knockback || drill || (State == Enums.PowerupState.MegaMushroom && singlejump))
            return;

        bool topSpeed = Mathf.Abs(body.velocity.x) + 0.5f > (runningMaxSpeed * (invincible > 0 ? 2 : 1));
        bool spinner = onSpinner && !holding;
        bool canSpecialJump = inputs.jumpHeld && properJump && !flying && topSpeed && landing < 0.45f && !holding && !triplejump && !crouching && invincible <= 0 && ((body.velocity.x < 0 && !facingRight) || (body.velocity.x > 0 && facingRight)) && !Physics2D.Raycast(body.position + new Vector2(0, 0.1f), Vector2.up, 1f, ONLY_GROUND_MASK);

        if (!(CurrentState?.OnJump(this, bounce, spinner, ref canSpecialJump) ?? true))
            return;

        if (bounce || (jump && (onGround || koyoteTime < 0.07f) && !startedSliding)) {
            koyoteTime = 1;
            jumpBuffer = 0;
            skidding = false;
            turnaround = false;
            sliding = false;
            alreadyGroundpounded = false;
            groundpound = false;
            groundpoundCounter = 0;
            drill = false;
            flying &= bounce;

            if (spinner) {
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Voice_SpinnerLaunch);
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.World_Spinner_Launch);
                body.velocity = new Vector2(body.velocity.x, spinnerLaunchVelocity);
                flying = true;
                onGround = false;
                crouching = false;
                return;
            }


            float vel = State switch {
                Enums.PowerupState.MegaMushroom => megaJumpVelocity,
                _ => jumpVelocity + Mathf.Abs(body.velocity.x) / 8f,
            };
            float jumpBoost = 0;

            if (canSpecialJump && singlejump) {
                //Double jump
                singlejump = false;
                doublejump = true;
                triplejump = false;
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Voice_DoubleJump, (byte) Random.Range(1, 3));
            } else if (canSpecialJump && doublejump) {
                //Triple Jump
                singlejump = false;
                doublejump = false;
                triplejump = true;
                jumpBoost = 0.5f;
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Voice_TripleJump);
            } else {
                //Normal jump
                singlejump = true;
                doublejump = false;
                triplejump = false;
            }
            body.velocity = new Vector2(body.velocity.x, vel + jumpBoost);
            onGround = false;
            doGroundSnap = false;
            body.position += Vector2.up * 0.075f;
            properJump = true;
            jumping = true;

            if (!bounce) {
                //play jump sound
                Enums.Sounds sound = State switch {
                    Enums.PowerupState.MiniMushroom => Enums.Sounds.Powerup_MiniMushroom_Jump,
                    Enums.PowerupState.MegaMushroom => Enums.Sounds.Powerup_MegaMushroom_Jump,
                    _ => Enums.Sounds.Player_Sound_Jump,
                };
                photonView.RPC("PlaySound", RpcTarget.All, sound);
            }
            bounce = false;
        }
    }

    void HandleWalkingRunning(float delta) {
        if (groundpound || groundpoundCounter > 0 || sliding || knockback || pipeEntering || !(wallJumpTimer <= 0 || onGround))
            return;

        if (!(CurrentState?.OnWalkRun(this, delta) ?? true))
            return;

        iceSliding = false;
        if (!inputs.left && !inputs.right) {
            skidding = false;
            turnaround = false;
            if (doIceSkidding)
                iceSliding = true;
        }

        if (Mathf.Abs(body.velocity.x) < 0.5f || !onGround)
            skidding = false;

        if (inputs.left ^ inputs.right)
            return;

        float invincibleSpeedBoost = invincible > 0 ? 2f : 1;
        float airPenalty = onGround ? 1 : 0.5f;
        float xVel = body.velocity.x;
        float runSpeedTotal = runningMaxSpeed * invincibleSpeedBoost;
        float walkSpeedTotal = walkingMaxSpeed * invincibleSpeedBoost;
        bool reverseSlowing = onGround && ((inputs.left && body.velocity.x > 0.02) || (inputs.right && body.velocity.x < -0.02));
        float reverseFloat = reverseSlowing && doIceSkidding ? 0.4f : 1;
        float turnaroundSpeedBoost = turnaround && !reverseSlowing ? 5 : 1;
        float stationarySpeedBoost = Mathf.Abs(body.velocity.x) <= 0.005f ? 1f : 1f;
        float propellerBoost = propellerTimer > 0 ? 2.5f : 1;

        if ((crouching && !onGround) || !crouching) {

            if (inputs.left) {
                if (functionallyRunning && !crouching && !flying && xVel <= -(walkingMaxSpeed - 0.3f)) {
                    skidding = false;
                    turnaround = false;
                    if (xVel > -runSpeedTotal) {
                        float change = propellerBoost * invincibleSpeedBoost * invincibleSpeedBoost * invincibleSpeedBoost * turnaroundSpeedBoost * runningAcceleration * airPenalty * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * -1, 0);
                    }
                } else {
                    if (xVel > -walkSpeedTotal) {
                        float change = propellerBoost * invincibleSpeedBoost * reverseFloat * turnaroundSpeedBoost * walkingAcceleration * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * -1, 0);

                        if (State != Enums.PowerupState.MegaMushroom && reverseSlowing && xVel > runSpeedTotal - 2) {
                            skidding = true;
                            turnaround = true;
                            facingRight = true;
                        }
                    } else {
                        turnaround = false;
                    }
                }
            }
            if (inputs.right) {
                if (functionallyRunning && !crouching && !flying && xVel >= (walkingMaxSpeed - 0.3f)) {
                    skidding = false;
                    turnaround = false;
                    if (xVel < runSpeedTotal) {
                        float change = propellerBoost * invincibleSpeedBoost * invincibleSpeedBoost * invincibleSpeedBoost * turnaroundSpeedBoost * runningAcceleration * airPenalty * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * 1, 0);
                    }
                } else {
                    if (xVel < walkSpeedTotal) {
                        float change = propellerBoost * invincibleSpeedBoost * reverseFloat * turnaroundSpeedBoost * walkingAcceleration * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * 1, 0);

                        if (State != Enums.PowerupState.MegaMushroom && reverseSlowing && xVel < -runSpeedTotal + 2) {
                            skidding = true;
                            turnaround = true;
                            facingRight = false;
                        }
                    } else {
                        turnaround = false;
                    }
                }
            }
        } else {
            turnaround = false;
            skidding = false;
        }

        if (onGround)
            body.velocity = new Vector2(body.velocity.x, 0);
    }

    bool HandleStuckInBlock() {
        if (!body || hitboxes[0] == null)
            return false;
        Vector2 checkPos = body.position + new Vector2(0, hitboxes[0].size.y / 4f);
        if (!Utils.IsTileSolidAtWorldLocation(checkPos)) {
            stuckInBlock = false;
            return false;
        }
        stuckInBlock = true;
        body.gravityScale = 0;
        onGround = true;
        if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(0, 0.3f))) {
            transform.position = body.position = new Vector2(body.position.x, Mathf.Floor((checkPos.y + 0.3f) * 2) / 2 + 0.1f);
            return false;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos - new Vector2(0, 0.3f))) {
            transform.position = body.position = new Vector2(body.position.x, Mathf.Floor((checkPos.y - 0.3f) * 2) / 2 - 0.1f);
            return false;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(0.25f, 0))) {
            body.velocity = Vector2.right * 2f;
            return true;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(-0.25f, 0))) {
            body.velocity = Vector2.left * 2f;
            return true;
        }

        RaycastHit2D rightRaycast = Physics2D.Raycast(checkPos, Vector2.right, 15, ONLY_GROUND_MASK);
        RaycastHit2D leftRaycast = Physics2D.Raycast(checkPos, Vector2.left, 15, ONLY_GROUND_MASK);
        float rightDistance = float.MaxValue, leftDistance = float.MaxValue;
        if (rightRaycast)
            rightDistance = rightRaycast.distance;
        if (leftRaycast)
            leftDistance = leftRaycast.distance;
        if (rightDistance <= leftDistance) {
            body.velocity = Vector2.right * 2f;
        } else {
            body.velocity = Vector2.left * 2f;
        }
        return true;
    }

    void TickCounter(ref float counter, float min, float delta) {
        counter = Mathf.Max(min, counter - delta);
    }

    void TickCounters() {
        float delta = Time.fixedDeltaTime;
        if (!pipeEntering)
            TickCounter(ref invincible, 0, delta);

        TickCounter(ref throwInvincibility, 0, delta);
        TickCounter(ref jumpBuffer, 0, delta);
        if (giantStartTimer <= 0)
            TickCounter(ref giantTimer, 0, delta);
        TickCounter(ref giantStartTimer, 0, delta);
        TickCounter(ref groundpoundCounter, 0, delta);
        TickCounter(ref giantEndTimer, 0, delta);
        TickCounter(ref groundpoundDelay, 0, delta);
        TickCounter(ref hitInvincibilityCounter, 0, delta);
        TickCounter(ref knockbackTimer, 0, delta);

        TickCounter(ref wallSlideTimer, 0, delta);
        TickCounter(ref wallJumpTimer, 0, delta);

        if (onGround)
            TickCounter(ref landing, 0, -delta);
    }

    [PunRPC]
    public void FinishMegaMario(bool success) {
        if (success) {
            PlaySoundEverywhere(Enums.Sounds.Player_Voice_MegaMushroom);
        } else {
            //hit a wall, cancel
            giantSavedVelocity = Vector2.zero;
            State = Enums.PowerupState.Large;
            giantEndTimer = giantStartTime;
            stationaryGiantEnd = true;
            storedPowerup = (Powerup) Resources.Load("Scriptables/Powerups/MegaMushroom");
            giantTimer = 0;
            animator.enabled = true;
            animator.Play("mega-cancel", 1);
            PlaySound(Enums.Sounds.Player_Sound_PowerupReserveStore);
        }
        body.isKinematic = false;
    }

    IEnumerator CheckForGiantStartupTiles() {
        HandleGiantTiles(false);
        yield return null;
        RaycastHit2D hit = Physics2D.BoxCast(body.position + new Vector2(0, 1.6f), new Vector2(0.45f, 2.6f), 0, Vector2.zero, 0, ONLY_GROUND_MASK);
        photonView.RPC("FinishMegaMario", RpcTarget.All, !(bool)hit);
    }
    void HandleFacingDirection() {
        //Changing facing direction
        if (sliding || groundpound)
            return;

        if (doIceSkidding /* && !inShell */ && !groundpound) {
            if (inputs.right || inputs.left)
                facingRight = inputs.right;
        } else if (giantStartTimer <= 0 && giantEndTimer <= 0 && !skidding && !(animator.GetCurrentAnimatorStateInfo(0).IsName("turnaround") || turnaround)) {
            if (knockback || (onGround && State != Enums.PowerupState.MegaMushroom && Mathf.Abs(body.velocity.x) > 0.05f)) {
                facingRight = body.velocity.x > 0;
            } else if (((wallJumpTimer <= 0 && !inputs.inShell) || giantStartTimer > 0) && (inputs.right || inputs.left)) {
                facingRight = inputs.right;
            }
            if (!inShell && ((Mathf.Abs(body.velocity.x) < 0.5f && crouching) || doIceSkidding) && (inputs.right || inputs.left))
                facingRight = inputs.right;
        }
    }
    void HandleMovement(float delta) {
        functionallyRunning = running || State == Enums.PowerupState.MegaMushroom;

        if (photonView.IsMine && body.position.y + transform.lossyScale.y < GameManager.Instance.GetLevelMinY()) {
            //death via pit
            photonView.RPC("Death", RpcTarget.All, true, false);
            return;
        }

        bool paused = GameManager.Instance.paused && photonView.IsMine;

        if (giantStartTimer > 0) {
            body.velocity = Vector2.zero;
            transform.position = body.position = previousFramePosition;
            if (giantStartTimer - delta <= 0) {
                //start by checking bounding
                giantStartTimer = 0;
                if (photonView.IsMine)
                    StartCoroutine(CheckForGiantStartupTiles());
            } else {
                body.isKinematic = true;
                if (animator.GetCurrentAnimatorClipInfo(1).Length <= 0 || animator.GetCurrentAnimatorClipInfo(1)[0].clip.name != "mega-scale")
                    animator.Play("mega-scale", 1);
            }
            return;
        }
        if (giantEndTimer > 0 && stationaryGiantEnd) {
            body.velocity = Vector2.zero;
            body.isKinematic = true;
            transform.position = body.position = previousFramePosition;

            if (giantEndTimer - delta <= 0) {
                hitInvincibilityCounter = 2f;
                body.velocity = giantSavedVelocity;
                animator.enabled = true;
                body.isKinematic = false;
            }
            return;
        }


        //pipes > stuck in block, else the animation gets janked.
        if (pipeEntering || giantStartTimer > 0 || (giantEndTimer > 0 && stationaryGiantEnd) || animator.GetBool("pipe"))
            return;
        if (HandleStuckInBlock())
            return;


        //Powerup Movement Update
        CurrentState?.OnMovementUpdate(this, delta);

        if (State == Enums.PowerupState.MegaMushroom) {
            HandleGiantTiles(true);
            if (onGround && singlejump) {
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust", body.position);
                singlejump = false;
            }
            invincible = 0;
        }

        //Pipes
        if (!paused) {
            DownwardsPipeCheck();
            UpwardsPipeCheck();
        }

        if (knockback) {
            if (bounce && photonView.IsMine)
                photonView.RPC("ResetKnockback", RpcTarget.All);

            wallSlideLeft = false;
            wallSlideRight = false;
            crouching = false;
            body.velocity -= body.velocity * (delta * 2f);
            if (photonView.IsMine && onGround && Mathf.Abs(body.velocity.x) < 0.2f && knockbackTimer <= 0)
                photonView.RPC("ResetKnockback", RpcTarget.All);
            if (holding) {
                holding.photonView.RPC("Throw", RpcTarget.All, !facingRight, true);
                holding = null;
            }
        }

        //activate blocks jumped into
        if (hitRoof) {
            body.velocity = new Vector2(body.velocity.x, Mathf.Min(body.velocity.y, -0.1f));
            bool tempHitBlock = false;
            foreach (Vector3Int tile in tilesJumpedInto) {
                int temp = InteractWithTile(tile, InteractableTile.InteractionDirection.Up);
                if (temp != -1)
                    tempHitBlock |= temp == 1;
            }
            if (tempHitBlock && State == Enums.PowerupState.MegaMushroom) {
                cameraController.screenShakeTimer = 0.15f;
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.World_Block_Bump);
            }
        }

        inputs.right = joystick.x > analogDeadzone && !paused;
        inputs.left = joystick.x < -analogDeadzone && !paused;
        inputs.down= joystick.y < -analogDeadzone && !paused;
        inputs.up = joystick.y > analogDeadzone && !paused;
        inputs.jumpPressed = jumpBuffer > 0 && (onGround || koyoteTime < 0.07f || wallSlideLeft || wallSlideRight) && !paused;

        if (!inputs.down)
            alreadyGroundpounded = false;

        if (holding) {
            wallSlideLeft = false;
            wallSlideRight = false;
            if (holding.CompareTag("frozencube")) {
                holding.holderOffset = new Vector2(0f, State >= Enums.PowerupState.Large ? 1.2f : 0.5f);
            } else {
                holding.holderOffset = new Vector2((facingRight ? 1 : -1) * 0.25f, State >= Enums.PowerupState.Large ? 0.5f : 0.25f);
            }
        }

        //throwing held item
        if (holding && !functionallyRunning) {
            ThrowHeldItem();
        }

        //Ground
        if (onGround) {
            if (photonView.IsMine && hitRoof && crushGround && body.velocity.y <= 0.1 && State != Enums.PowerupState.MegaMushroom)
                //Crushed.
                photonView.RPC("Powerdown", RpcTarget.All, true);

            koyoteTime = 0;
            wallSlideLeft = false;
            wallSlideRight = false;
            jumping = false;
            if (drill)
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust", body.position);

            if (onSpinner && Mathf.Abs(body.velocity.x) < 0.3f && !holding) {
                Transform spnr = onSpinner.transform;
                if (body.position.x > spnr.transform.position.x + 0.02f) {
                    body.position -= new Vector2(0.01f * 60f, 0) * Time.fixedDeltaTime;
                } else if (body.position.x < spnr.transform.position.x - 0.02f) {
                    body.position += new Vector2(0.01f * 60f, 0) * Time.fixedDeltaTime;
                }
            }
        } else {
            koyoteTime += delta;
            landing = 0;
            skidding = false;
            turnaround = false;
            if (!jumping)
                properJump = false;
        }

        HandleCrouching();
        HandleWallslide();
        HandleSlopes();

        if (crouch && !alreadyGroundpounded)
            HandleGroundpoundStart(left, right);
        HandleGroundpound();

        if ((wallJumpTimer <= 0 || onGround) && !(groundpound && !onGround)) {
            //Normal walking/running
            HandleWalkingRunning(left, right);

            //Jumping
            HandleJumping(jump);
        }


        if (State == Enums.PowerupState.MegaMushroom && giantTimer <= 0) {
            giantEndTimer = giantStartTime / 2f;
            State = Enums.PowerupState.Large;
            stationaryGiantEnd = false;
            hitInvincibilityCounter = 3f;
            PlaySoundEverywhere(Enums.Sounds.Powerup_MegaMushroom_End);
            body.velocity = new(body.velocity.x, body.velocity.y > 0 ? (body.velocity.y / 3f) : body.velocity.y);
        }

        HandleSlopes();
        HandleSliding();
        HandleFacingDirection();



        //slow-rise check
        if (flying || propeller) {
            body.gravityScale = flyingGravity;
        } else {
            float gravityModifier = State switch {
                Enums.PowerupState.MiniMushroom => 0.4f,
                _ => 1,
            };
            float slowriseModifier = State switch {
                Enums.PowerupState.MegaMushroom => 3f,
                _ => 1f,
            };
            if (body.velocity.y > 2.5) {
                if (jump || jumpHeld || State == Enums.PowerupState.MegaMushroom) {
                    body.gravityScale = slowriseGravity * slowriseModifier;
                } else {
                    body.gravityScale = normalGravity * 1.5f * gravityModifier;
                }
            } else if (onGround || groundpound) {
                body.gravityScale = 0f;
            } else {
                body.gravityScale = normalGravity * (gravityModifier / 1.2f);
            }
        }

        if (groundpound && groundpoundCounter <= 0)
            body.velocity = new Vector2(0f, -groundpoundVelocity);

        if (!inShell && onGround && !(sliding && Mathf.Abs(floorAngle) > slopeSlidingAngle)) {
            bool abovemax;
            float invincibleSpeedBoost = invincible > 0 ? 2f : 1;
            bool uphill = Mathf.Sign(floorAngle) == Mathf.Sign(body.velocity.x);
            float max = (functionallyRunning ? runningMaxSpeed : walkingMaxSpeed) * invincibleSpeedBoost * (uphill ? (1 - (Mathf.Abs(floorAngle) / 270f)) : 1);
            if (knockback) {
                abovemax = true;
            } else if (!sliding && inputs.left && !crouching) {
                abovemax = body.velocity.x < -max;
            } else if (!sliding && inputs.right && !crouching) {
                abovemax = body.velocity.x > max;
            } else if (Mathf.Abs(floorAngle) > slopeSlidingAngle) {
                abovemax = Mathf.Abs(body.velocity.x) > (Mathf.Abs(floorAngle) / 30f);
            } else {
                abovemax = true;
            }
            //Friction...
            if (abovemax) {
                body.velocity *= 1 - (delta * tileFriction * (knockback ? 3f : 4f) * (sliding ? 0.4f : 1f));
                if (Mathf.Abs(body.velocity.x) < 0.15f)
                    body.velocity = new Vector2(0, body.velocity.y);
            }
        }

        //Terminal velocity
        IPowerupState.TerminalVelocityResult tvr = CurrentState?.HandleTerminalVelocity(this) ?? IPowerupState.TerminalVelocityResult.None;

        if ((tvr & IPowerupState.TerminalVelocityResult.HandledX) == 0) {
            // didn't handle x
        }
        if ((tvr & IPowerupState.TerminalVelocityResult.HandledY) == 0) {
            // didn't handle y
            if (flying) {
                if (drill) {
                    body.velocity = new(Mathf.Max(-1.5f, Mathf.Min(1.5f, body.velocity.x)), -drillVelocity);
                } else {
                    body.velocity = new Vector2(Mathf.Clamp(body.velocity.x, -walkingMaxSpeed, walkingMaxSpeed), Mathf.Max(body.velocity.y, -flyingTerminalVelocity));
                }
            } else if (wallSlideLeft || wallSlideRight) {
                body.velocity = new Vector2(body.velocity.x, Mathf.Max(body.velocity.y, wallslideSpeed));

            } else if (!groundpound) {
                body.velocity = new Vector2(body.velocity.x, Mathf.Max(body.velocity.y, terminalVelocity * terminalVelocityModifier));

            }


        }

        
        float terminalVelocityModifier = State switch {
            Enums.PowerupState.MiniMushroom => 0.65f,
            Enums.PowerupState.MegaMushroom => 3f,
            _ => 1f,
        };
        if (wallSlideLeft || wallSlideRight) {
            body.velocity = new Vector2(body.velocity.x, Mathf.Max(body.velocity.y, wallslideSpeed));
        } else if (!groundpound) {
            body.velocity = new Vector2(body.velocity.x, Mathf.Max(body.velocity.y, terminalVelocity * terminalVelocityModifier));
        }
        if (!onGround)
            body.velocity = new Vector2(Mathf.Max(-runningMaxSpeed * 1.2f, Mathf.Min(runningMaxSpeed * 1.2f, body.velocity.x)), body.velocity.y);


        if (crouching || sliding || skidding) {
            wallSlideLeft = false;
            wallSlideRight = false;
        }
        if (onGround) {
            flying = false;
            drill = false;
            if (triplejump && landing == 0 && !(inputs.left || inputs.right) && !groundpound) {
                if (!doIceSkidding)
                    body.velocity = Vector2.zero;
                animator.Play("jumplanding", State >= Enums.PowerupState.Large ? 1 : 0);
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust", body.position);
            }
            if (landing > 0.2f) {
                singlejump = false;
                doublejump = false;
                triplejump = false;
            }
        }
    }

    void ThrowHeldItem() {
        if (!holding)
            return;

        bool throwLeft = !facingRight;
        if (inputs.left ^ inputs.right)
            throwLeft = inputs.left;

        if (photonView.IsMine)
            holding.photonView.RPC("Throw", RpcTarget.All, throwLeft, inputs.down);

        if (!inputs.down && !knockback) {
            PlaySound(Enums.Sounds.Player_Voice_WallJump, 2);
            throwInvincibility = 0.5f;
            animator.SetTrigger("throw");
        }
        holdingOld = holding;
        holding = null;
    }

    void HandleGroundpoundStart(bool left, bool right) {
        if (!photonView.IsMine)
            return;
        if (onGround || knockback || groundpound || drill
            || holding || crouching || sliding
            || wallSlideLeft || wallSlideRight || groundpoundDelay > 0)

            return;

        if (!(CurrentState?.OnGroundpound(this) ?? true))
            return;

        if (!flying && (left || right))
            return;

        if (flying) {
            //start drill
            if (body.velocity.y < 0) {
                drill = true;
                hitBlock = true;
            }
        } else {
            //start groundpound
            //check if high enough above ground
            if (Physics2D.BoxCast(body.position, hitboxes[0].size * transform.localScale, 0, Vector2.down, 0.15f * (State == Enums.PowerupState.MegaMushroom ? 2.5f : 1), ANY_GROUND_MASK))
                return;

            wallSlideLeft = false;
            wallSlideRight = false;
            groundpound = true;
            singlejump = false;
            doublejump = false;
            triplejump = false;
            hitBlock = true;
            sliding = false;
            body.velocity = Vector2.zero;
            groundpoundCounter = groundpoundTime * (State == Enums.PowerupState.MegaMushroom ? 1.5f : 1);
            photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Player_Sound_GroundpoundStart);
            alreadyGroundpounded = true;
            groundpoundDelay = 0.7f;
        }
    }

    void HandleGroundpound() {
        if (!(photonView.IsMine && onGround && (groundpound || drill) && hitBlock))
            return;
        bool tempHitBlock = false, hitAnyBlock = false;
        foreach (Vector3Int tile in tilesStandingOn) {
            int temp = InteractWithTile(tile, InteractableTile.InteractionDirection.Down);
            if (temp != -1) {
                hitAnyBlock = true;
                tempHitBlock |= temp == 1;
            }
        }
        hitBlock = tempHitBlock;
        if (drill) {
            flying &= hitBlock;
            propeller &= hitBlock;
            drill = hitBlock;
            if (hitBlock)
                onGround = false;
        } else {
            //groundpound
            if (hitAnyBlock) {
                if (State != Enums.PowerupState.MegaMushroom) {
                    Enums.Sounds sound = State switch {
                        Enums.PowerupState.MiniMushroom => Enums.Sounds.Powerup_MiniMushroom_Groundpound,
                        _ => Enums.Sounds.Player_Sound_GroundpoundLanding,
                    };
                    photonView.RPC("PlaySound", RpcTarget.All, sound);
                    photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust", body.position);
                } else {
                    cameraController.screenShakeTimer = 0.15f;
                }
            }
            if (hitBlock) {
                koyoteTime = 1.5f;
            } else if (State == Enums.PowerupState.MegaMushroom) {
                photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Powerup_MegaMushroom_Groundpound);
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust", body.position);
                cameraController.screenShakeTimer = 0.35f;
            }
        }
    }

    public bool CanPickup() {
        if (!(CurrentState?.CanPickupItem(this) ?? true))
            return false;

        return State != Enums.PowerupState.MiniMushroom && !skidding && !turnaround && !holding && running && !flying && !crouching && !dead && !wallSlideLeft && !wallSlideRight && !doublejump && !triplejump;
    }
    void OnDrawGizmos() {
        if (!body)
            return;

        Gizmos.DrawRay(body.position, body.velocity);
        Gizmos.DrawCube(body.position + new Vector2(0, hitboxes[0].size.y / 2f * transform.lossyScale.y) + (body.velocity * Time.fixedDeltaTime), hitboxes[0].size * transform.lossyScale);
    }
}