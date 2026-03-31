using System;
using System.Collections.Generic;
using UnityEngine;

public class CarController : MonoBehaviour {

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

    private float m_horizontalInput;
    private float m_verticalInput;
    private float m_currentSteerAngle;
    private bool m_isBraking;
    private Rigidbody rb;

    public List<Wheel> wheels;
    public float maxSteerAngle;
    public float motorForce;
    public float brakeForce;
    public float turnSpeed;

    public void GetInput() {
        m_horizontalInput = Input.GetAxis("Horizontal");
        m_verticalInput = Input.GetAxisRaw("Vertical");
    }

    public void SetInput(float horizontal, float vertical, bool braking) {
        m_horizontalInput = horizontal;
        m_verticalInput = vertical;
        m_isBraking = braking;
    }

    public void StopCompletely() {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        m_verticalInput = 0f;
        m_horizontalInput = 0f;
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
        if(Input.GetKey(KeyCode.Space) || m_isBraking) {
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
        //GetInput();
        LogSpeed();
        
    }

    private void Start() {
        int carLayer = LayerMask.NameToLayer("Car");
        Physics.IgnoreLayerCollision(carLayer, carLayer, true);
        rb = GetComponent<Rigidbody>();   
    }

    private void LogSpeed() {
        float speedMetersPerSecond = rb.linearVelocity.magnitude;
        //float speedKPH = speedMetersPerSecond * 3.6f; 
        //Debug.Log($"Speed: {Mathf.RoundToInt(speedKPH)} KM/H");
    }

    private void FixedUpdate() {
        Steer();
        Accelerate();
        Brake();
        AnimateWheels();
    }
}