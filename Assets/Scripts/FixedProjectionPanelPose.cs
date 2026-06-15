using UnityEngine;

public sealed class FixedProjectionPanelPose : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] float distance = 1.35f;
    [SerializeField] Vector2 viewportPosition = new(0.24f, 0.2f);
    [SerializeField] float viewportWidth = 0.19f;
    [SerializeField] RectTransform panelRect;

    void LateUpdate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        transform.position = targetCamera.ViewportToWorldPoint(new Vector3(viewportPosition.x, viewportPosition.y, distance));
        transform.rotation = targetCamera.transform.rotation;

        if (panelRect == null)
            panelRect = transform as RectTransform;

        if (panelRect == null || panelRect.rect.width <= 0.0f)
            return;

        float halfFovY = targetCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
        float halfFovX = Mathf.Atan(Mathf.Tan(halfFovY) * targetCamera.aspect);
        float worldWidth = 2.0f * distance * Mathf.Tan(halfFovX) * viewportWidth;
        float scale = worldWidth / panelRect.rect.width;
        transform.localScale = Vector3.one * scale;
    }
}
