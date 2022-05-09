using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

namespace NSMB.Player.State {
    public class ShellPowerupState : IPowerupState {

        private bool inShell, right;
        public void OnMovementUpdate(PlayerController player, float delta) {
            if (inShell) {
                if (!player.inputs.sprint) {
                    inShell = false;
                }
            } else {
                if (player.onGround && player.inputs.sprint && !player.holding && Mathf.Abs(player.body.velocity.x) >= player.runningMaxSpeed - 0.25f && player.landing > 0.15f) {
                    inShell = true;
                    right = player.body.velocity.x > 0;
                }
            }

            if (inShell) {
                player.crouching = true;
                if (player.photonView.IsMine && (player.hitLeft || player.hitRight)) {
                    foreach (var tile in player.tilesHitSide)
                        player.InteractWithTile(tile, InteractableTile.InteractionDirection.Up);
                    right = player.hitLeft;
                    player.photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.World_Block_Bump);
                }
            }
        }
        public bool OnWalkRun(PlayerController player, float delta) {
            if (!inShell)
                return true;

            player.body.velocity = new Vector2(player.runningMaxSpeed * (right ? 1 : -1), player.body.velocity.y);
            return false;
        }
        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player) {
            return IPowerupState.TerminalVelocityResult.None;
        }
        public void OnAnimatorUpdate(PlayerController player, float delta) { }
        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint) { }

        //cancellable events
        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker) {
            if (inShell && fireball)
                return false;

            inShell = false;
            return true;
        }
        public bool OnPipeEnter(PlayerController player, PipeManager pipe) {
            inShell = false;
            return true;
        }
        public bool OnWallslideStart(PlayerController player, bool right) {
            return !inShell;
        }
        public bool OnJump(PlayerController player, bool enemyBounce, bool spinner, ref bool specialJump) {
            inShell &= !spinner;
            specialJump &= inShell;

            return true;
        }
        public bool OnGroundpound(PlayerController player) {
            return !inShell;
        }
        public bool OnGroundpoundLand(PlayerController player, bool continueThrough) {
            return true;
        }
        public bool OnCrouch(PlayerController player, bool startCrouch) {
            if (inShell)
                return false;

            player.crouching = startCrouch == true;

            //crouch start sound
            if (startCrouch) {
                player.photonView.RPC("PlaySound", RpcTarget.All, Enums.Sounds.Powerup_BlueShell_Enter);
            }
            return false;
        }

        public bool CanPickupItem(PlayerController player) {
            return !inShell;
        }
        public void OnPowerup(PlayerController player) { }

        public void OnPowerdown(PlayerController player) {
            inShell = false;
        }
    }
}