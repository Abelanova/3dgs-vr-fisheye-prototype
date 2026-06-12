using UnityEngine;

public sealed class DesktopVrPreviewController : MonoBehaviour
{
    [SerializeField] Transform cameraTransform;
    [SerializeField] float moveSpeed = 3.0f;
    [SerializeField] float fastMoveMultiplier = 3.0f;
    [SerializeField] float lookSensitivity = 0.12f;

    float pitch;
    float yaw;

    void Reset()
    {
        cameraTransform = Camera.main ? Camera.main.transform : null;
    }

    void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        var euler = transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = cameraTransform != null ? NormalizeAngle(cameraTransform.localEulerAngles.x) : 0.0f;
    }

    void Update()
    {
        if (cameraTransform == null)
            return;

        UpdateLook();
        UpdateMove();
    }

    void UpdateLook()
    {
        if (!Input.GetMouseButton(1))
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity * 10.0f;
        pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity * 10.0f;
        pitch = Mathf.Clamp(pitch, -85.0f, 85.0f);

        transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0.0f, 0.0f);
    }

    void UpdateMove()
    {
        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        float x = Axis(KeyCode.D, KeyCode.A);
        float z = Axis(KeyCode.W, KeyCode.S);
        float y = Axis(KeyCode.E, KeyCode.Q);

        var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        var move = right * x + forward * z + Vector3.up * y;
        if (move.sqrMagnitude > 1.0f)
            move.Normalize();

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? fastMoveMultiplier : 1.0f);
        transform.position += move * (speed * Time.deltaTime);
    }

    static float Axis(KeyCode positive, KeyCode negative)
    {
        return (Input.GetKey(positive) ? 1.0f : 0.0f) - (Input.GetKey(negative) ? 1.0f : 0.0f);
    }

    static float NormalizeAngle(float angle)
    {
        return angle > 180.0f ? angle - 360.0f : angle;
    }
}
