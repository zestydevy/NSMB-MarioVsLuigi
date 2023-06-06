using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct PowerUpQueue
{
    Enums.PowerupState mPreviousState;
    Enums.PowerupState mCurrentState;
    Enums.Sounds mSound;

    public PowerUpQueue(Enums.PowerupState previous, Enums.PowerupState current, Enums.Sounds powerUpSound) {
        mPreviousState = previous;
        mCurrentState = current;
        mSound = powerUpSound;
    }

    public Enums.PowerupState getPrevState() { return mPreviousState; }
    public Enums.PowerupState getCurrentState() { return mCurrentState; }
    public Enums.Sounds getSound() { return mSound; }
}
