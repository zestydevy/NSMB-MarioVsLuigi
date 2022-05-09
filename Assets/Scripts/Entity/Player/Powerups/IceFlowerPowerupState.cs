using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSMB.Player.State {
    public class IceFlowerPowerupState : ProjectilePowerupState {

        public IceFlowerPowerupState() : base("Prefabs/Iceball", "fireball", 2, Vector2.one / 2) { }

    }
}
