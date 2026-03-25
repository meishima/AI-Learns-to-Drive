using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.MLAgents.SideChannels;
using UnityEngine;

public class CarController : MonoBehaviour {

    private float m_horizontalInput;
    private float m_verticalInput;
    private Rigidbody rb;
    private float m_currentSteerAngle;

    public enum Axel {
        Front,
        Back
    }

    [Serializable]
    public struct Wheel {
        public WheelCollider wheelCollider;
        public Transform wheelTransform;
        public Axel axel;
    }

    public List<Wheel> wheels;
    public float maxSteerAngle;
    public float motorForce;
    public float brakeForce;
    public float turnSpeed;

    public void GetInput() {
        m_horizontalInput = Input.GetAxis("Horizontal");
        m_verticalInput = Input.GetAxisRaw("Vertical");
    }

    private void Steer() {
        float targetAngle = maxSteerAngle * m_horizontalInput;
        m_currentSteerAngle = Mathf.MoveTowards(m_currentSteerAngle, targetAngle, turnSpeed * Time.fixedDeltaTime);

        foreach(var wheel in wheels) {
            if(wheel.axel == Axel.Front) {
                wheel.wheelCollider.steerAngle = m_currentSteerAngle;
            }
        }
    }

    private void Accelerate() {
        foreach(var wheel in wheels) {
            if(wheel.axel == Axel.Back) {
                wheel.wheelCollider.motorTorque = m_verticalInput * motorForce;
            }
        }
    }

    private void Brake() {
        if(Input.GetKey(KeyCode.Space) || m_verticalInput == 0) {
            foreach(var wheel in wheels) {
                wheel.wheelCollider.brakeTorque = brakeForce;
            }
        } else {
            foreach(var wheel in wheels) {
                wheel.wheelCollider.brakeTorque = 0;
            }
        }
    }

    private void AnimateWheels() {
        foreach(var wheel in wheels) {
            Quaternion rot;
            Vector3 pos;
            wheel.wheelCollider.GetWorldPose(out pos, out rot);
            wheel.wheelTransform.position = pos;
            wheel.wheelTransform.rotation = rot;
        }
    }

    private void Update() {
        GetInput();
        LogSpeed();
    }

    private void Start() {
        rb = GetComponent<Rigidbody>();   
    }

    private void LogSpeed() {
    float speedMetersPerSecond = rb.linearVelocity.magnitude;
    float speedKPH = speedMetersPerSecond * 3.6f; 
    Debug.Log($"Speed: {Mathf.RoundToInt(speedKPH)} KM/H");
    }

    private void FixedUpdate() {
        Steer();
        Accelerate();
        Brake();
        AnimateWheels();
    }
}