using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// The only file that reads devices, so the game runs whether the project uses the new Input System or the old Input Manager.
public static class Controls
{
#if ENABLE_INPUT_SYSTEM
    static Keyboard K => Keyboard.current;
    static Mouse M => Mouse.current;
    static bool Held(Key k) => K != null && K[k].isPressed;
    static bool Down(Key k) => K != null && K[k].wasPressedThisFrame;

    public static Vector2 Move => new Vector2((Held(Key.D) ? 1 : 0) - (Held(Key.A) ? 1 : 0), (Held(Key.W) ? 1 : 0) - (Held(Key.S) ? 1 : 0));
    // Scaled to match the old "Mouse X" axis (pixels * 0.1) so sensitivity values mean the same on both backends.
    public static Vector2 Look => M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f;
    public static bool Jump => Down(Key.Space);
    public static bool Sprint => Held(Key.LeftShift);
    public static bool CrouchHeld => Held(Key.LeftCtrl) || Held(Key.C);
    public static bool CrouchDown => Down(Key.LeftCtrl) || Down(Key.C);
    public static bool Fire => M != null && M.leftButton.isPressed;
    public static bool FireDown => M != null && M.leftButton.wasPressedThisFrame;
    public static bool Aim => M != null && M.rightButton.isPressed;
    public static bool Reload => Down(Key.R);
    public static int WeaponSlot => Down(Key.Digit1) ? 0 : Down(Key.Digit2) ? 1 : Down(Key.Digit3) ? 2 : -1;
    public static float Scroll => M == null ? 0f : M.scroll.ReadValue().y;
    public static bool Pause => Down(Key.Escape);
    public static bool Confirm => Down(Key.Enter);
    public static bool ToggleOverlay => Down(Key.F1);
    public static bool ToggleFeel => Down(Key.F2);
    public static bool TogglePooling => Down(Key.F3);
#else
    static bool Held(KeyCode k) => Input.GetKey(k);
    static bool Down(KeyCode k) => Input.GetKeyDown(k);

    public static Vector2 Move => new Vector2((Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.A) ? 1 : 0), (Held(KeyCode.W) ? 1 : 0) - (Held(KeyCode.S) ? 1 : 0));
    public static Vector2 Look => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
    public static bool Jump => Down(KeyCode.Space);
    public static bool Sprint => Held(KeyCode.LeftShift);
    public static bool CrouchHeld => Held(KeyCode.LeftControl) || Held(KeyCode.C);
    public static bool CrouchDown => Down(KeyCode.LeftControl) || Down(KeyCode.C);
    public static bool Fire => Input.GetMouseButton(0);
    public static bool FireDown => Input.GetMouseButtonDown(0);
    public static bool Aim => Input.GetMouseButton(1);
    public static bool Reload => Down(KeyCode.R);
    public static int WeaponSlot => Down(KeyCode.Alpha1) ? 0 : Down(KeyCode.Alpha2) ? 1 : Down(KeyCode.Alpha3) ? 2 : -1;
    public static float Scroll => Input.mouseScrollDelta.y;
    public static bool Pause => Down(KeyCode.Escape);
    public static bool Confirm => Down(KeyCode.Return);
    public static bool ToggleOverlay => Down(KeyCode.F1);
    public static bool ToggleFeel => Down(KeyCode.F2);
    public static bool TogglePooling => Down(KeyCode.F3);
#endif
}
