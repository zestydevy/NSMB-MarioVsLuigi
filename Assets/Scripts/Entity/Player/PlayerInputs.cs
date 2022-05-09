using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ExitGames.Client.Photon;

public class PlayerInputs {

    //Analog input(s)
    public Vector2 joystick;

    //Discrete inputs
    public bool left, right, up, down, sprint;

    //Jumping inputs
    public bool jumpPressed, jumpHeld;


    public static readonly byte[] outArray = new byte[9];
    public static short Serialize(StreamBuffer outStream, object obj) {
        PlayerInputs inputs = (PlayerInputs) obj;
        lock (outArray) {
            int index = 0;

            Protocol.Serialize(inputs.joystick.x, outArray, ref index);
            Protocol.Serialize(inputs.joystick.y, outArray, ref index);

            byte inputFlags = 0;
            SetBitInByte(ref inputFlags, inputs.left, 0);
            SetBitInByte(ref inputFlags, inputs.right, 1);
            SetBitInByte(ref inputFlags, inputs.up, 2);
            SetBitInByte(ref inputFlags, inputs.down, 3);
            SetBitInByte(ref inputFlags, inputs.sprint, 4);
            SetBitInByte(ref inputFlags, inputs.jumpPressed, 5);
            SetBitInByte(ref inputFlags, inputs.jumpHeld, 6);

            outArray[index++] = inputFlags;
            outStream.Write(outArray, 0, outArray.Length);
        }

        return (short) outArray.Length;
    }

    public static object Deserialize(StreamBuffer inStream, short length) {
        PlayerInputs inputs = new();

        lock (outArray) {
            inStream.Read(outArray, 0, outArray.Length);

            int index = 0;
            Protocol.Deserialize(out inputs.joystick.x, outArray, ref index);
            Protocol.Deserialize(out inputs.joystick.y, outArray, ref index);

            byte inputFlags = outArray[index++];
            GetBitInByte(out inputs.left, inputFlags, 0);
            GetBitInByte(out inputs.right, inputFlags, 1);
            GetBitInByte(out inputs.up, inputFlags, 2);
            GetBitInByte(out inputs.down, inputFlags, 3);
            GetBitInByte(out inputs.sprint, inputFlags, 4);
            GetBitInByte(out inputs.jumpPressed, inputFlags, 5);
            GetBitInByte(out inputs.jumpHeld, inputFlags, 6);
        }

        return inputs;
    }

    private static void SetBitInByte(ref byte flags, bool value, int position) {
        if (value)
            flags |= (byte) (1 << position);
        else
            flags &= (byte) ~(1 << position);
    }

    private static void GetBitInByte(out bool ret, byte flags, int position) {
        ret = (flags & (byte) (1 << position)) == 1;
    }
}
