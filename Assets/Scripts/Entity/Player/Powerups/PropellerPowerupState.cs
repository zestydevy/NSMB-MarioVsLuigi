using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSMB.Player.State {
    public class PropellerPowerupState : IPowerupState {

        //Variables
        private readonly float spinTime = 0.5f, launchVelocity = 6;

        private float launchTimer, spinTimer, drillBuffer;
        private bool propeller, usedThisJump, powerupButtonState;

        public void OnMovementUpdate(PlayerController player, float delta) {
            player.functionallyRunning |= propeller;

            //Cancelling states
            bool dontSetUsedJump = player.groundpound;
            if (dontSetUsedJump || player.onGround || player.wallSlideLeft || player.wallSlideRight || player.knockback) {
                ResetValues(!dontSetUsedJump);
                return;
            }

            //Landing
            if (player.onGround && launchTimer <= 0) {
                ResetValues(true);
            }

            //Launching
            Utils.TickTimer(ref launchTimer, delta, min: 0);
            if (launchTimer > 0)
                player.body.velocity = new Vector2(player.body.velocity.x, launchVelocity - (launchTimer < .4f ? (1 - (launchTimer / .4f)) * launchVelocity : 0));

            //Spinning
            Utils.TickTimer(ref spinTimer, delta, min: 0);
            if (powerupButtonState && (propeller || usedThisJump) && spinTimer < spinTime / 4f && !player.drill && player.body.velocity.y < -0.1f) {
                spinTimer = spinTime;
                player.PlaySound(Enums.Sounds.Powerup_PropellerMushroom_Spin);
            }

            //Drilling Start
            if (!player.drill && player.inputs.down && launchTimer < 0.6f) {
                launchTimer = 0;
                spinTimer = 0;
                player.drill = true;
                player.hitBlock = true;
            }

            //Drilling Cancel
            if (player.inputs.down) {
                Utils.TickTimer(ref drillBuffer, delta, min: 0);
                if (drillBuffer <= 0)
                    player.drill = false;
                else
                    drillBuffer = 0.15f;
            }
        }

        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player) {

            Rigidbody2D b = player.body;
            Vector2 v = b.velocity;

            if (propeller) {
                if (player.drill) {
                    b.velocity = new(Mathf.Clamp(v.x, -1.5f, 1.5f), -player.drillVelocity);
                } else {
                    float htv = player.walkingMaxSpeed * 1.18f + (launchTimer * 2f);
                    b.velocity = new Vector2(Mathf.Clamp(v.x, -htv, htv), Mathf.Max(v.y, spinTimer > 0 ? -1.5f : -2f));
                }
                return IPowerupState.TerminalVelocityResult.HandledX | IPowerupState.TerminalVelocityResult.HandledY;
            }

            return IPowerupState.TerminalVelocityResult.None;
        }

        public void OnAnimatorUpdate(PlayerController player, float delta) { }

        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint) {
            if (!pressed || bySprint)
                return;

            //Keep track of state for auto-spinning
            powerupButtonState = pressed;

            //Restrictions
            if (propeller || player.groundpound || player.knockback || player.holding || player.crouching || player.sliding || player.wallJumpTimer > 0)
                return;

            if (usedThisJump) {
                spinTimer = spinTime;
                player.PlaySound(Enums.Sounds.Powerup_PropellerMushroom_Spin);
            } else {
                player.body.velocity = new Vector2(player.body.velocity.x, launchVelocity);
                launchTimer = 1f;
                player.PlaySound(Enums.Sounds.Powerup_PropellerMushroom_Start);

                player.animator.Play("propeller_up", 1);
                propeller = true;
                player.flying = false;
                player.drill = false;
                player.crouching = false;

                if (player.onGround) {
                    player.onGround = false;
                    player.doGroundSnap = false;
                    player.body.position += Vector2.up * 0.15f;
                }
                usedThisJump = true;
            }
        }

        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker) {
            ResetValues(true);
            return true;
        }
        public bool OnPipeEnter(PlayerController player, PipeManager pipe) {
            ResetValues(true);
            return true;
        }
        public bool OnJump(PlayerController player, bool enemyBounce, bool spinner, ref bool specialJump) {
            return true;
        }
        public bool OnWallslideStart(PlayerController player, bool right) {
            if (!propeller)
                return true;

            return launchTimer <= 0;
        }
        public bool OnGroundpound(PlayerController player) {
            if (!propeller)
                return true;

            //start drill
            if (launchTimer < 0.5f) {
                launchTimer = 0;
                spinTimer = 0;
                player.drill = true;
            }
            return false;
        }
        public bool OnGroundpoundLand(PlayerController player, bool continueThrough) {
            if (!continueThrough)
                ResetValues(true);

            return true;
        }
        public bool OnCrouch(PlayerController player, bool crouchStart) {
            return !propeller;
        }

        public bool CanPickupItem(PlayerController player) {
            return !propeller;
        }

        public void OnPowerup(PlayerController player) { }
        public void OnPowerdown(PlayerController player) {
            ResetValues(true);
        }


        private void ResetValues(bool resetUsedJump) {
            propeller = false;
            launchTimer = 0;
            spinTimer = 0;
            if (resetUsedJump)
                usedThisJump = false;
        }
    }
}