using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

namespace NSMB.Player.State {

    public class ProjectilePowerupState : IPowerupState {

        private string projectilePrefab, animatorTrigger;
        private int projectileLimit = 2;
        private Vector2 projectileSpawnOffset = Vector2.one / 2;
        private readonly HashSet<GameObject> myProjectiles = new();

        public ProjectilePowerupState(string prefab, string trigger, int limit, Vector2 offset) {
            projectilePrefab = prefab;
            animatorTrigger = trigger;
            projectileLimit = limit;
            projectileSpawnOffset = offset;
        }

        public void OnMovementUpdate(PlayerController player, float delta) { }
        public IPowerupState.TerminalVelocityResult HandleTerminalVelocity(PlayerController player) {
            return IPowerupState.TerminalVelocityResult.None;
        }
        public void OnAnimatorUpdate(PlayerController player, float delta) { }
        public bool OnKnockback(PlayerController player, bool fromRight, int stars, bool fireball, int attacker) {
            return true;
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

        public void OnPowerupButton(PlayerController player, bool pressed, bool bySprint) {
            //Don't shoot on release
            if (!pressed)
                return;
            
            // Can we even shoot??
            if (player.wallSlideLeft || player.wallSlideRight || player.groundpound || player.triplejump || player.holding || player.flying || player.crouching || player.sliding)
                return;

            // Check for the projectile limit. Destroyed gameobjects automatically become nulls.
            myProjectiles.RemoveWhere(go => go == null);
            if (myProjectiles.Count == projectileLimit)
                return;

            // Spawn the prefab
            bool spawnRight = player.facingRight ^ player.animator.GetCurrentAnimatorStateInfo(0).IsName("turnaround");
            Vector2 spawnPosition = player.body.position + projectileSpawnOffset * (spawnRight ? Vector2.right : Vector2.left);
            GameObject projectile = PhotonNetwork.Instantiate(projectilePrefab, spawnPosition, Quaternion.identity, data: new object[] { !spawnRight });
            myProjectiles.Add(projectile);

            // Play animation
            if (animatorTrigger != null)
                player.animator.SetTrigger(animatorTrigger);
        }

        public void OnPowerup(PlayerController player) { }

        public void OnPowerdown(PlayerController player) { }

    }
}