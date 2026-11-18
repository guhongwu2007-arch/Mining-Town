using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MiningTown
{
    public static class PrototypeInput
    {
        public static Vector2 Movement()
        {
            Vector2 axis = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return axis;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis.x -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis.x += 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) axis.y -= 1;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) axis.y += 1;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) axis.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) axis.x += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) axis.y -= 1;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) axis.y += 1;
#endif
            return axis.normalized;
        }

        public static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        public static bool MenuPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.M);
#else
            return false;
#endif
        }

        // IMGUI does not receive pointer events with the new input backend alone.
        // The demo draws the UI with IMGUI, but reads those clicks explicitly.
        public static bool PointerReleased(out Vector2 position)
        {
            position = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null) return false;
            position = mouse.position.ReadValue();
            return mouse.leftButton.wasReleasedThisFrame;
#else
            return false;
#endif
        }
    }
}
