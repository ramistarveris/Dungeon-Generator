using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class DungeonPlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Min(.1f)] public float walkSpeed = 6f;
    [Min(.1f)] public float sprintSpeed = 10f;
    [Min(.1f)] public float jumpHeight = 1.3f;
    public float gravity = -22f;

    [Header("Mouse Look")]
    [Range(.01f, 1f)] public float mouseSensitivity = .13f;
    public float maxLookAngle = 85f;

    private CharacterController character;
    private Transform view;
    private float verticalVelocity;
    private float pitch;

    public void Initialize(Transform firstPersonView)
    {
        view = firstPersonView;
        character = GetComponent<CharacterController>();
    }

    private void Awake()
    {
        character = GetComponent<CharacterController>();
    }

    private void Start()
    {
        CaptureCursor();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || character == null || view == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            ReleaseCursor();

        Mouse mouse = Mouse.current;
        if (mouse != null && Cursor.lockState != CursorLockMode.Locked &&
            mouse.leftButton.wasPressedThisFrame)
            CaptureCursor();

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            transform.Rotate(0f, delta.x * mouseSensitivity, 0f);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity,
                -maxLookAngle, maxLookAngle);
            view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        bool sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float speed = sprint ? sprintSpeed : walkSpeed;
        Vector3 horizontal = (transform.right * input.x +
            transform.forward * input.y) * speed;

        if (character.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (character.isGrounded && keyboard.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;
        character.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    private static void CaptureCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }
}
