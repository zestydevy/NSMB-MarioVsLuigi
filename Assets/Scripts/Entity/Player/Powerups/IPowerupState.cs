using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSMB.Player.State {

    public interface IPowerupState {
        public void OnMovementUpdate(PlayerController player, float delta);
        public bool OnWalkRun(PlayerController player, float delta);
        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player);
        public void OnAnimatorUpdate(PlayerController player, float delta);
        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint);

        //cancellable events
        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker);
        public bool OnPipeEnter(PlayerController player, PipeManager pipe);
        public bool OnWallslideStart(PlayerController player, bool right);
        public bool OnJump(PlayerController player, bool enemyBounce, bool spinner, ref bool specialJump);
        public bool OnGroundpound(PlayerController player);
        public bool OnGroundpoundLand(PlayerController player, bool continueThrough);
        public bool OnCrouch(PlayerController player, bool startCrouch);

        //
        public bool CanPickupItem(PlayerController player);

        //powerup/down events
        public void OnPowerup(PlayerController player);
        public void OnPowerdown(PlayerController player);
        

        public enum TerminalVelocityResult {
            None = 0,
            HandledX = 1,
            HandledY = 2
        }
    }
}