using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSMB.Player.State { 
    public class DefaultPowerupState : IPowerupState {
        public void OnMovementUpdate(PlayerController player, float delta) { }
        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player) {
            return IPowerupState.TerminalVelocityResult.None;
        }
        public void OnAnimatorUpdate(PlayerController player, float delta) { }
        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint) { }

        //cancellable events
        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker) {
            return true;
        }
        public bool OnPipeEnter(PlayerController player, PipeManager pipe) {
            return !pipe.miniOnly;
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

        //powerup/down events
        public void OnPowerup(PlayerController player) { }
        public void OnPowerdown(PlayerController player) { }
    }
}
