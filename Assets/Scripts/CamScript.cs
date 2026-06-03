using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CamScript : MonoBehaviour
{
    public float sensitivity;
    public float normalSpeed, sprintSpeed;
    float currentSpeed;

    private bool isFreeCamActive = false;
    private Agent agentSelected;
    public Canvas canvas;
    
    void Update()
    {
        //Free Cam Toggle Logic

        //if (Input.GetMouseButton(1))     ->    NOTE FOR SELF: Might need to press right click twice. Old Input system
        if(Mouse.current.rightButton.wasPressedThisFrame)  // NOTE FOR SELF: New input system
        {
            isFreeCamActive = !isFreeCamActive;
        }

        if (isFreeCamActive)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.Locked;
            canvas.enabled = true;
            Movement();
            Rotation();
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            canvas.enabled = false;
        }

        // RayCast Logic
        if (Mouse.current.leftButton.wasPressedThisFrame)  // NOTE FOR SELF: New input system
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            Debug.Log("Shoot ray");
            if (Physics.Raycast(ray, out hit))
            {
                Debug.Log("Ray hit");
                GameObject clickedGameObject = hit.collider.gameObject;

                Agent agent = clickedGameObject.GetComponent<Agent>();
                if (agent != null)
                {
                    Debug.Log("Selected agent: " + agent.npcName); // TODO: Create a GUI to display NPC's info
                    Debug.Log("Personality Type: " + agent.personalityType);
                    Debug.Log("Values: " + agent.PrintValues());
                    agentSelected = agent;
                }
                else
                {
                    Debug.Log("Hit anything else: " + clickedGameObject.name);

                    // Move NPC logic
                    if (agentSelected != null)
                    {
                        agentSelected.MoveToDestination(hit.point);
                    }
                }
            }
        }

    }

    void Rotation()
    {
        Vector3 mouseInput = new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0);
        transform.Rotate(mouseInput * sensitivity * Time.deltaTime * 50); // TODO: Correct this magic number
        Vector3 eulerRotation = transform.rotation.eulerAngles;
        transform.rotation = Quaternion.Euler(eulerRotation.x, eulerRotation.y, 0);
    }

    void Movement()
    {
        Vector3 input = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed = sprintSpeed;
        }
        else
        {
            currentSpeed = normalSpeed;
        }
        transform.Translate(input * currentSpeed * Time.deltaTime);
    }
}