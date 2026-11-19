using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(200)] // 等鼠标脚本更新完位置再检测
public class OpenMailOnHover : MonoBehaviour
{
    public Transform screenOpen;       // 拖入 Screen Open
    public GameObject openedMail;      // 拖入显示邮件正文的物体

    private SpriteRenderer notice;
    private Transform cursor;
    private bool opened;

    void Awake()
    {
        notice = GetComponent<SpriteRenderer>();
        if (!screenOpen) screenOpen = transform.parent;
    }

    void LateUpdate()
    {
        if (opened || !screenOpen || !openedMail) return;

        // 兼容前面的 3 秒动画：通知还透明时，不触发。
        if (!notice.enabled || !notice.sprite || notice.color.a < 0.95f)
            return;

        // 之前的鼠标脚本会在 Screen Open 下自动生成这个光标。
        if (!cursor)
            cursor = screenOpen.Find("Mouse Cursor (Runtime)");

        if (!cursor || !cursor.gameObject.activeInHierarchy) return;

        // 把箭头尖端换算到通知图片自己的坐标中。
        // 即使笔记本盖子旋转或通知缩放，检测区域也会跟着走。
        Vector3 tip = transform.InverseTransformPoint(cursor.position);

        // 只检测 Sprite 网格覆盖的区域，而不是整块屏幕。
        if (!notice.localBounds.Contains(new Vector3(
                tip.x, tip.y, notice.localBounds.center.z)))
            return;

        opened = true;
        openedMail.SetActive(true);   // 显示邮件正文
        gameObject.SetActive(false);  // 隐藏右下角通知
    }
}
