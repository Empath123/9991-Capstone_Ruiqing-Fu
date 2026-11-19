using UnityEngine;

// Place on the same Screen Open that LaptopMouseXR.screenOpen references.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class LaptopEmailHover : MonoBehaviour
{
    [SerializeField] private SpriteRenderer emailIcon;
    [SerializeField] private GameObject emailContent;

    private Transform cursor;
    private bool opened;

    private void Awake()
    {
        if (!emailIcon || !emailContent || emailIcon.gameObject == emailContent ||
            !emailIcon.transform.IsChildOf(transform) || !emailContent.transform.IsChildOf(transform) ||
            emailContent.transform == transform || emailIcon.transform == transform ||
            emailContent.transform.IsChildOf(emailIcon.transform) ||
            emailIcon.transform.IsChildOf(emailContent.transform))
        {
            Debug.LogError("LaptopEmailHover: assign separate Email and Email Content children of Screen Open.", this);
            enabled = false;
            return;
        }
        emailContent.SetActive(false);
        // Existing notification logic owns the 3-second delay.
    }

    private void LateUpdate()
    {
        if (opened || !emailIcon.enabled || !emailIcon.gameObject.activeInHierarchy || !emailIcon.sprite ||
            emailIcon.forceRenderingOff || emailIcon.color.a <= 0.01f) return;

        // LaptopMouseXR creates this child when the desktop becomes ready.
        // Its transform origin is the arrow tip, not the arrow's centre.
        if (!cursor) cursor = transform.Find("Mouse Cursor (Runtime)");
        if (!cursor || !cursor.gameObject.activeInHierarchy) return;

        // Local XY test works on a tilted lid and ignores the tiny depth
        // offset between the cursor mesh and the notification sprite.
        Vector3 p = emailIcon.transform.InverseTransformPoint(cursor.position);
        Bounds b = emailIcon.localBounds;
        if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) return;

        opened = true;
        emailIcon.gameObject.SetActive(false);
        emailContent.SetActive(true);
    }
}
