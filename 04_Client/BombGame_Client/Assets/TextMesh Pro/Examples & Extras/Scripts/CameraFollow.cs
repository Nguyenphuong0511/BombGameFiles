using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;           // Nhân vật mà camera sẽ bám theo
    public float smoothSpeed = 0.125f; // Độ mượt khi di chuyển camera
    public Vector3 offset = new Vector3(0, 0, -10); // Camera luôn giữ khoảng cách Z cố định

    void LateUpdate()
    {
        if (target != null)
        {
            // Chỉ lấy vị trí X-Y của nhân vật, giữ Z cố định
            Vector3 desiredPosition = new Vector3(target.position.x, target.position.y, offset.z);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;
        }
    }

    // Hàm để gán nhân vật mới cho camera theo dõi
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
