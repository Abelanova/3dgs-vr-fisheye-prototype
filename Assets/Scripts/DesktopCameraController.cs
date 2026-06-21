using UnityEngine;
using UnityEngine.InputSystem;

public sealed class DesktopCameraController : MonoBehaviour
{
    [SerializeField] float moveSpeed = 2.0f;
    [SerializeField] float fastMoveMultiplier = 4.0f;
    [SerializeField] float lookSensitivity = 0.12f;

    float pitch;

    void OnEnable()
    {
        pitch = transform.eulerAngles.x;
        if (pitch > 180.0f)
            pitch -= 360.0f;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null)
            return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 lookDelta = mouse.delta.ReadValue() * lookSensitivity;
            pitch = Mathf.Clamp(pitch - lookDelta.y, -89.0f, 89.0f);
            transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y + lookDelta.x, 0.0f);
        }

        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed) input.z += 1.0f;
        if (keyboard.sKey.isPressed) input.z -= 1.0f;
        if (keyboard.dKey.isPressed) input.x += 1.0f;
        if (keyboard.aKey.isPressed) input.x -= 1.0f;
        if (keyboard.eKey.isPressed) input.y += 1.0f;
        if (keyboard.qKey.isPressed) input.y -= 1.0f;

        float speed = moveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
            speed *= fastMoveMultiplier;

        Vector3 movement = transform.right * input.x + transform.forward * input.z + Vector3.up * input.y;
        if (movement.sqrMagnitude > 1.0f)
            movement.Normalize();

        transform.position += movement * speed * Time.unscaledDeltaTime;
    }
}
