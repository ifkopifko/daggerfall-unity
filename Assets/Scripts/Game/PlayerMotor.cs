using UnityEngine;
using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Serialization;

namespace DaggerfallWorkshop.Game
{
    //
    // Using FPSWalkerEnhanced from below community wiki entry.
    // http://wiki.unity3d.com/index.php?title=FPSWalkerEnhanced
    //
    // Extended for moving platforms, and ceiling hits, and other tweaks.
    //
    [RequireComponent(typeof(PlayerSpeedChanger))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        // Moving platform support
        Transform activePlatform;
        Vector3 activeLocalPlatformPoint;
        Vector3 activeGlobalPlatformPoint;
        //Vector3 lastPlatformVelocity;
        Quaternion activeLocalPlatformRotation;
        Quaternion activeGlobalPlatformRotation;

        public float standingHeight = 1.78f;
        public float eyeHeight = 0.09f;         // Eye height is 9cm below top of capsule.
        public float crouchingHeight = 0.45f;
        public float crouchingJumpDelta = 0.8f;
        bool isCrouching = false;
        //bool wasCrouching = false;

        // TODO: Placeholder integration of horse & cart riding - using same speed for cart to simplify PlayerMotor integration
        // and avoid adding any references to TransportManager.
        public float ridingHeight = 2.6f;   // Height of a horse plus seated rider. (1.6m + 1m)
        bool isRiding = false;
        private bool riding = false;

        // If true, diagonal speed (when strafing + moving forward or back) can't exceed normal move speed; otherwise it's about 1.4 times faster
        public bool limitDiagonalSpeed = true;

        // If checked, the run key toggles between running and walking. Otherwise player runs if the key is held down and walks otherwise
        // There must be a button set up in the Input Manager called "Run"
        public bool toggleRun = false;

        public float jumpSpeed = 8.0f;
        public float gravity = 20.0f;

        // Units that player can fall before a falling damage function is run. To disable, type "infinity" in the inspector
        public float fallingDamageThreshold = 10.0f;

        // If checked, then the player can change direction while in the air
        public bool airControl = false;

        // Player must be grounded for at least this many physics frames before being able to jump again; set to 0 to allow bunny hopping
        public int antiBunnyHopFactor = 1;

        public float systemTimerUpdatesPerSecond = .055f; // Number of updates per second by the system timer at memory location 0x46C.
                                                          // Used for timing various things in classic.

        // FixedUpdate is too choppy to give smooth camera movement. This handles a smooth following child transform.
        public Transform smoothFollower;                // The Transform that follows; will lerp to this Transform's position.
        public float smoothFollowerLerpSpeed = 25.0f;   // Multiplied by dt.
        Vector3 smoothFollowerPrevWorldPos;
        bool smoothFollowerReset = true;

        [HideInInspector, NonSerialized]
        public CharacterController controller;

        //private Camera mainCamera;
        //private float defaultCameraHeight;

        private Vector3 moveDirection = Vector3.zero;
        private bool grounded = false;
        private Transform myTransform;
        private float speed;
        private float fallStartLevel;
        private bool falling;
        private int jumpTimer;
        private bool jumping = false;
        private bool standingStill = false;

        private ClimbingMotor climbingMotor;
        private PlayerHeightChanger heightChanger;
        private PlayerSpeedChanger speedChanger;
        private FrictionMotor frictionMotor;

        private CollisionFlags collisionFlags = 0;

        private bool cancelMovement = false;

        LevitateMotor levitateMotor;
        float freezeMotor = 0;

        public bool IsGrounded
        {
            get { return grounded; }
        }

        public float Speed
        {
            get { return speed; }
        }

        public bool IsRunning
        {
            get { return speed == speedChanger.GetRunSpeed(speedChanger.GetBaseSpeed()); }
        }

        public bool IsStandingStill
        {
            get { return standingStill; }
        }

        public bool IsJumping
        {
            get { return jumping; }
        }

        public bool IsCrouching
        {
            get { return isCrouching; }
            set { isCrouching = value; }
        }

        public bool IsRiding
        {
            get { return isRiding; }
            set { isRiding = value; }
        }

        public bool IsMovingLessThanHalfSpeed
        {
            get
            {
                if (IsStandingStill)
                {
                    return true;
                }
                if (isCrouching)
                {
                    return (speedChanger.GetWalkSpeed(GameManager.Instance.PlayerEntity) / 2) >= speed;
                }
                return (speedChanger.GetBaseSpeed() / 2) >= speed;
            }
        }

        public Transform ActivePlatform
        {
            get { return activePlatform; }
        }

        /// <summary>
        /// Cancels all movement impulses next frame.
        /// Used to scrub movement impulse when player dies, opens inventory, or loads game.
        /// Flag will be lowered again after movement cleared.
        /// </summary>
        public bool CancelMovement
        {
            get { return cancelMovement; }
            set { cancelMovement = value; }
        }

        /// <summary>
        /// Freeze motor for an amount of time in seconds.
        /// Used by teleport action to prevent player from falling when teleport is part of a physics change.
        /// It can take a few frames for physics to catch up.
        /// </summary>
        public float FreezeMotor
        {
            get { return freezeMotor; }
            set { freezeMotor = value; }
        }

        public Vector3 MoveDirection
        {
            get
            {
                return moveDirection;
            }
        }

        void Start()
        {
            controller = GetComponent<CharacterController>();
            speedChanger = GetComponent<PlayerSpeedChanger>();
            myTransform = transform;
            speed = speedChanger.GetBaseSpeed();
            jumpTimer = antiBunnyHopFactor;
            climbingMotor = GetComponent<ClimbingMotor>();
            heightChanger = GetComponent<PlayerHeightChanger>();
            levitateMotor = GetComponent<LevitateMotor>();
            frictionMotor = GetComponent<FrictionMotor>();

            // Allow for resetting specific player state on new game or when game starts loading
            SaveLoadManager.OnStartLoad += SaveLoadManager_OnStartLoad;
            StartGameBehaviour.OnNewGame += StartGameBehaviour_OnNewGame;
        }

        void FixedUpdate()
        {
            // Clear movement
            if (cancelMovement)
            {
                moveDirection = Vector3.zero;
                cancelMovement = false;
                ClearActivePlatform();
                ClearFallingDamage();
                return;
            }

            // Handle freeze movement
            if (freezeMotor > 0)
            {
                freezeMotor -= Time.deltaTime;
                if (freezeMotor <= 0)
                {
                    freezeMotor = 0;
                    CancelMovement = true;
                }
                return;
            }

            // Handle climbing
            climbingMotor.ClimbingCheck(ref collisionFlags);

            if (climbingMotor.IsClimbing)
            {
                falling = false;
            }

            // Do nothing if player levitating/swimming or climbing - replacement motor will take over movement for levitating/swimming
            if (levitateMotor && (levitateMotor.IsLevitating || levitateMotor.IsSwimming) || climbingMotor.IsClimbing)
                return;

            float inputX = InputManager.Instance.Horizontal;
            float inputY = InputManager.Instance.Vertical;
            // If both horizontal and vertical are used simultaneously, limit speed (if allowed), so the total doesn't exceed normal move speed
            float inputModifyFactor = (inputX != 0.0f && inputY != 0.0f && limitDiagonalSpeed) ? .7071f : 1.0f;

            // Cancel all movement input if player is paralyzed
            // Player should still be able to fall or move with platforms
            if (GameManager.Instance.PlayerEntity.IsParalyzed)
            {
                inputX = 0;
                inputY = 0;
            }

            // Player assumed to be in movement for now
            standingStill = false;

            if (grounded)
            {
                // Set standing still while grounded flag
                // Casting moveDirection to a Vector2 so constant downward force of gravity not included in magnitude
                standingStill = (new Vector2(moveDirection.x, moveDirection.z).magnitude == 0);

                if (jumping)
                    jumping = false;

                // If we were falling, and we fell a vertical distance greater than the threshold, run a falling damage routine
                if (falling)
                {
                    falling = false;
                    float fallDistance = fallStartLevel - myTransform.position.y;
                    if (fallDistance > fallingDamageThreshold)
                        FallingDamageAlert(fallDistance);
                    else if (fallDistance > fallingDamageThreshold / 2f)
                        BadFallDetected(fallDistance);
                    //if (myTransform.position.y < fallStartLevel - fallingDamageThreshold)
                    //    FallingDamageAlert(fallDistance);
                }

                // Get walking/crouching/riding speed
                speed = speedChanger.GetBaseSpeed();

                if (!riding)
                {
                    if (!isCrouching && heightChanger.HeightAction != HeightChangeAction.DoStanding) // don't set to standing height while croucher is standing the player
                        controller.height = standingHeight;

                    try
                    {
                        // If running isn't on a toggle, then use the appropriate speed depending on whether the run button is down
                        if (!toggleRun && InputManager.Instance.HasAction(InputManager.Actions.Run))
                            speed = speedChanger.GetRunSpeed(speed);
                    }
                    catch
                    {
                        speed = speedChanger.GetRunSpeed(speed);
                    }
                }

                // Handle sneak key. Reduces movement speed to half, then subtracts 1 in classic speed units
                if (InputManager.Instance.HasAction(InputManager.Actions.Sneak))
                {
                    speed /= 2;
                    speed -= (1 / PlayerSpeedChanger.classicToUnitySpeedUnitRatio);
                }

                // checks if sliding and applies movement to moveDirection if true
                frictionMotor.MoveIfSliding(ref moveDirection);

                try
                {
                    // Jump! But only if the jump button has been released and player has been grounded for a given number of frames
                    if (!InputManager.Instance.HasAction(InputManager.Actions.Jump))
                        jumpTimer++;
                    //if (!Input.GetButton("Jump"))
                    //    jumpTimer++;
                    else if (jumpTimer >= antiBunnyHopFactor)
                    {
                        moveDirection.y = jumpSpeed;
                        jumpTimer = 0;
                        jumping = true;

                        // Modify crouching jump speed
                        if (isCrouching)
                            moveDirection.y *= crouchingJumpDelta;
                    }
                    else
                        jumping = false;
                }
                catch
                {
                }
            }
            else
            {
                // If we stepped over a cliff or something, set the height at which we started falling
                if (!falling)
                {
                    falling = true;
                    fallStartLevel = myTransform.position.y;
                }

                // If air control is allowed, check movement but don't touch the y component
                if (airControl && frictionMotor.PlayerControl)
                {
                    moveDirection.x = inputX * speed * inputModifyFactor;
                    moveDirection.z = inputY * speed * inputModifyFactor;
                    moveDirection = myTransform.TransformDirection(moveDirection);
                }
            }

            // Apply gravity
            moveDirection.y -= gravity * Time.deltaTime;

            // If we hit something above us AND we are moving up, reverse vertical movement
            if ((controller.collisionFlags & CollisionFlags.Above) != 0)
            {
                if (moveDirection.y > 0)
                    moveDirection.y = -moveDirection.y;
            }

            // Moving platform support
            if (activePlatform != null)
            {
                var newGlobalPlatformPoint = activePlatform.TransformPoint(activeLocalPlatformPoint);
                var moveDistance = (newGlobalPlatformPoint - activeGlobalPlatformPoint);
                if (moveDistance != Vector3.zero)
                    controller.Move(moveDistance);
                //lastPlatformVelocity = (newGlobalPlatformPoint - activeGlobalPlatformPoint) / Time.deltaTime;

                // If you want to support moving platform rotation as well:
                var newGlobalPlatformRotation = activePlatform.rotation * activeLocalPlatformRotation;
                var rotationDiff = newGlobalPlatformRotation * Quaternion.Inverse(activeGlobalPlatformRotation);

                // Prevent rotation of the local up vector
                rotationDiff = Quaternion.FromToRotation(rotationDiff * transform.up, transform.up) * rotationDiff;

                transform.rotation = rotationDiff * transform.rotation;
            }
            //else
            //{
            //    lastPlatformVelocity = Vector3.zero;
            //}

            activePlatform = null;

            // Move the controller, and set grounded true or false depending on whether we're standing on something
            collisionFlags = controller.Move(moveDirection * Time.deltaTime);

            grounded = (collisionFlags & CollisionFlags.Below) != 0;

            // Moving platforms support
            if (activePlatform != null)
            {
                activeGlobalPlatformPoint = transform.position;
                activeLocalPlatformPoint = activePlatform.InverseTransformPoint(transform.position);

                // If you want to support moving platform rotation as well:
                activeGlobalPlatformRotation = transform.rotation;
                activeLocalPlatformRotation = Quaternion.Inverse(activePlatform.rotation) * transform.rotation;
            }
        }

        // Reset moving platform logic to new player position
        public void ClearActivePlatform()
        {
            activePlatform = null;
        }

        // Call this when floating origin ticks on Y
        // to ensure player doesn't die by jumping right at threshold
        public void AdjustFallStart(float y)
        {
            if (falling)
            {
                fallStartLevel += y;
            }
        }

        /// <summary>
        /// Attempts to find the ground position below player, even if player is jumping/falling
        /// </summary>
        /// <param name="distance">Distance to fire ray.</param>
        /// <returns>Hit point on surface below player, or player position if hit not found in distance.</returns>
        public Vector3 FindGroundPosition(float distance = 10)
        {
            RaycastHit hit;
            Ray ray = new Ray(transform.position, Vector3.down);
            if (Physics.Raycast(ray, out hit, distance))
                return hit.point;

            return transform.position;
        }

        // Snap player to ground
        public bool FixStanding(float extraHeight = 0, float extraDistance = 0)
        {
            RaycastHit hit;
            Ray ray = new Ray(transform.position + (Vector3.up * extraHeight), Vector3.down);
            if (Physics.Raycast(ray, out hit, (controller.height * 2) + extraHeight + extraDistance))
            {
                // Position player at hit position plus just over half controller height up
                transform.position = hit.point + Vector3.up * (controller.height * 0.65f);
                return true;
            }

            return false;
        }

        // Gets distance between position and player
        public float DistanceToPlayer(Vector3 position)
        {
            return Vector3.Distance(transform.position, position);
        }

        void Update()
        {
            // Do nothing if player levitating - replacement motor will take over movement.
            // Don't return here for swimming because player should still be able to crouch when swimming.
            if (levitateMotor && levitateMotor.IsLevitating)
                return;

            if (isRiding && !riding)
            {
                heightChanger.HeightAction = HeightChangeAction.DoMounting;
                riding = true;
            }
            else if (!isRiding && riding)
            {
                heightChanger.HeightAction = HeightChangeAction.DoDismounting;
                riding = false;
            }
            else if (!isRiding)
            {
                try
                {
                    // If the run button is set to toggle, then switch between walk/run speed. (We use Update for this...
                    // FixedUpdate is a poor place to use GetButtonDown, since it doesn't necessarily run every frame and can miss the event)
                    if (toggleRun && grounded && InputManager.Instance.HasAction(InputManager.Actions.Run))
                        speed = (speed == speedChanger.GetBaseSpeed() ? speedChanger.GetRunSpeed(speed) : speedChanger.GetBaseSpeed());
                    //if (toggleRun && grounded && Input.GetButtonDown("Run"))
                    //    speed = (speed == walkSpeed ? runSpeed : walkSpeed);
                }
                catch
                {
                    speed = speedChanger.GetRunSpeed(speed);
                }

                // Toggle crouching
                if (InputManager.Instance.ActionComplete(InputManager.Actions.Crouch))
                {
                    if (isCrouching)
                        heightChanger.HeightAction = HeightChangeAction.DoStanding;
                    else
                        heightChanger.HeightAction = HeightChangeAction.DoCrouching;
                }

            }

            if (smoothFollower != null && controller != null)
            {
                float distanceMoved = Vector3.Distance(smoothFollowerPrevWorldPos, smoothFollower.position);        // Assuming the follower is a child of this motor transform we can get the distance travelled.
                float maxPossibleDistanceByMotorVelocity = controller.velocity.magnitude * 2.0f * Time.deltaTime;   // Theoretically the max distance the motor can carry the player with a generous margin.
                float speedThreshold = speedChanger.GetRunSpeed(speed) * Time.deltaTime;                                         // Without question any distance travelled less than the running speed is legal.

                // NOTE: Maybe the min distance should also include the height different between crouching / standing.
                if (distanceMoved > speedThreshold && distanceMoved > maxPossibleDistanceByMotorVelocity)
                {
                    smoothFollowerReset = true;
                }

                if (smoothFollowerReset)
                {
                    smoothFollowerPrevWorldPos = transform.position;
                    smoothFollowerReset = false;
                }

                smoothFollower.position = Vector3.Lerp(smoothFollowerPrevWorldPos, transform.position, smoothFollowerLerpSpeed * Time.smoothDeltaTime);
                smoothFollowerPrevWorldPos = smoothFollower.position;
            }
        }

        // Store point that we're in contact with for use in FixedUpdate if needed
        /*void OnControllerColliderHit(ControllerColliderHit hit)
        {
            frictionMotor.ContactPoint = hit.point;

            // Don't consider enemies as moving platforms
            // Otherwise, if we're positioned just right on top of one, the player camera gets unstable
            // This still allows standing on enemies
            if (hit.collider.gameObject.GetComponent<DaggerfallEnemy>() != null)
                return;

            // Get active platform
            if (hit.moveDirection.y < -0.9 && hit.normal.y > 0.5)
                activePlatform = hit.collider.transform;
        }*/

        // If falling damage occured, this is the place to do something about it. You can make the player
        // have hitpoints and remove some of them based on the distance fallen, add sound effects, etc.
        void FallingDamageAlert(float fallDistance)
        {
            SendMessage("ApplyPlayerFallDamage", fallDistance, SendMessageOptions.DontRequireReceiver);
        }

        // This was a bad fall, but not enough to damage player.
        // Might want to play a sound or animation however.
        void BadFallDetected(float fallDistance)
        {
            SendMessage("HardFallAlert", fallDistance, SendMessageOptions.DontRequireReceiver);
        }

        public void ClearFallingDamage()
        {
            falling = false;
            fallStartLevel = transform.position.y;
        }

        #region Private Methods

        void ResetPlayerState()
        {
            // Cancel levitation at start of loading a new save game
            // This prevents levitation flag carrying over and effect system can still restore it if needed
            if (levitateMotor)
                levitateMotor.IsLevitating = false;
        }

        #endregion

        #region Event Handlers

        private void StartGameBehaviour_OnNewGame()
        {
            ResetPlayerState();
        }

        private void SaveLoadManager_OnStartLoad(SaveData_v1 saveData)
        {
            ResetPlayerState();
        }

        #endregion
    }
}
