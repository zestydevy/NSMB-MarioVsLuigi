using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSMB.Player.State {
    public class FireFlowerPowerupState : ProjectilePowerupState {

        public FireFlowerPowerupState() : base("Prefabs/Fireball", "fireball", 2, Vector2.one / 2) { }

    }
}