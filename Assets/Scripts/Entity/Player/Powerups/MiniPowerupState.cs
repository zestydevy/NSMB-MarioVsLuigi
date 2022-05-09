using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

namespace NSMB.Player.State {
    public class MiniPowerupState : IPowerupState {
        public void OnMovementUpdate(PlayerController player, float delta) { }
        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player) {




            return IPowerupState.TerminalVelocityResult.HandledY;
        }
        public void OnAnimatorUpdate(PlayerController player, float delta) { }
        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint) { }
        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker) {
            if (fireball)
                player.photonView.RPC("Powerdown", RpcTarget.All, false);

            return !fireball;
        }
        public bool OnPipeEnter(PlayerController player, PipeManager pipe) {
            return true;
        }
        public bool OnWallslideStart(PlayerController player, bool right) {
            return true;
        }
        public bool OnJump(PlayerController player, bool enemyBounce) {
            return true;
        }
        public bool OnGroundpound(PlayerController player) {
            return true;
        }
        public bool OnGroundpoundLand(PlayerController player, bool continueThrough) {
            return true;
        }
        public bool OnCrouch(PlayerController player, bool startCrouch) {
            return true;
        }

        public bool CanPickupItem(PlayerController player) {
            return false;
        }

        public void OnPowerup(PlayerController player) { }
        public void OnPowerdown(PlayerController player) { }


    }
}