using System;

// The physics behind the feel numbers. No UnityEngine here on purpose, so the maths can be checked outside the editor.
public static class Kinematics
{
    // v^2 = u^2 + 2as with v = 0 at the top of the jump  =>  u = sqrt(2gh)
    public static float TakeoffSpeed(float gravity, float height) => (float)Math.Sqrt(2 * gravity * height);

    // Up and back down: t = 2u / g
    public static float AirTime(float gravity, float height) => 2 * TakeoffSpeed(gravity, height) / gravity;

    // Inside a cone of half-angle A when dot(forward, toTarget) >= cos A, both vectors normalised.
    public static float ConeThreshold(float halfAngleDegrees) => (float)Math.Cos(halfAngleDegrees * Math.PI / 180);
}
