using System;
using System.Collections.Generic;
using UnityEngine;

public class CarController : MonoBehaviour {

    public enum Axel {
        Front,
        Back
    }

    [Serializable]
    public class Wheel {
        public WheelCollider wheelCollider;
        public Transform wheelTransform;
        public Axel axel;
    }

    private float m_horizontalInput;
    private float m_verticalInput;
    private float m_currentSteerAngle;
    private bool m_isBraking;
    private Rigidbody rb;

    private WheelCollider m_frontLeftWheel;
    private WheelCollider m_frontRightWheel;
    private WheelCollider m_backLeftWheel;
    private WheelCollider m_backRightWheel;
    private List<WheelCollider> m_frontWheels = new List<WheelCollider>();
    private List<WheelCollider> m_backWheels = new List<WheelCollider>();
    private float m_invHighSpeedThreshold;

    [Header("Core Car Settings")]
    public List<Wheel> wheels;
    public float maxSteerAngle = 35f;
    public float motorForce = 1500f;
    public float brakeForce = 3000f;
    public float turnSpeed = 5f;

    [Header("Advanced Physics Tuning")]
    public float centerOfMassOffset = -0.6f;
    public float downforceFactor = 50f;
    public float antiRollForce = 8000f;

    [Header("Speed-Sensitive Steering")]
    public float highSpeedSteerAngle = 12f;
    public float highSpeedThreshold = 120f;

    [Header("Electronic Stability Control (ESC)")]
    [Range(0f, 1f)]
    public float stabilityControl = 0.65f;

    [Header("Friction & Suspension Auto-Tuning")]
    public bool autoConfigureSuspension = true;
    public float tireGripStiffness = 2.3f;

    // ── Input ──────────────────────────────────────────────────

    public void GetInput() {
        m_horizontalInput = Input.GetAxis("Horizontal");
        m_verticalInput   = Input.GetAxisRaw("Vertical");
        m_isBraking       = Input.GetKey(KeyCode.Space);
    }

    public void SetInput(float horizontal, float vertical, bool braking) {
        m_horizontalInput = horizontal;
        m_verticalInput   = vertical;
        m_isBraking       = braking;
    }

    public void StopCompletely() {
        if (rb == null) rb = GetComponent<Rigidbody>();

        if (!rb.isKinematic) {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        m_verticalInput   = 0f;
        m_horizontalInput = 0f;
        m_isBraking       = false;
    }

    // ── Steering ───────────────────────────────────────────────

    private void Steer() {
        float speedKPH = rb.linearVelocity.magnitude * 3.6f;

        float speedFactor    = Mathf.Clamp01(speedKPH * m_invHighSpeedThreshold);
        float activeMaxSteer = maxSteerAngle + (highSpeedSteerAngle - maxSteerAngle) * speedFactor;

        float targetAngle = activeMaxSteer * m_horizontalInput;
        m_currentSteerAngle = Mathf.MoveTowards(m_currentSteerAngle, targetAngle, turnSpeed * Time.fixedDeltaTime);

        for (int i = 0; i < m_frontWheels.Count; i++) {
            m_frontWheels[i].steerAngle = m_currentSteerAngle;
        }
    }

    // ── Drive & Brakes ─────────────────────────────────────────

    private void ApplyDriveAndBrakes(Vector3 forward) {
        float localForwardSpeed = Vector3.Dot(rb.linearVelocity, forward);

        float motorTorque          = 0f;
        float currentBrakeForceFront = 0f;
        float currentBrakeForceBack  = 0f;

        float absVerticalInput = Mathf.Abs(m_verticalInput);

        if (m_isBraking) {
            currentBrakeForceFront = brakeForce * 0.3f;
            currentBrakeForceBack  = brakeForce * 1.5f;
        } else {
            if (m_verticalInput > 0.05f) {
                if (localForwardSpeed < -0.8f) {
                    // Counter-steer brake while reversing
                    currentBrakeForceFront = brakeForce * m_verticalInput * 1.2f;
                    currentBrakeForceBack  = brakeForce * m_verticalInput * 0.6f;
                } else {
                    motorTorque = m_verticalInput * motorForce;
                }
            } else if (m_verticalInput < -0.05f) {
                if (localForwardSpeed > 0.8f) {
                    // Engine-braking when reversing while moving forward
                    currentBrakeForceFront = brakeForce * absVerticalInput * 1.2f;
                    currentBrakeForceBack  = brakeForce * absVerticalInput * 0.6f;
                } else {
                    const float reverseSpeedLimit = 7f;
                    if (localForwardSpeed > -reverseSpeedLimit) {
                        motorTorque = m_verticalInput * (motorForce * 0.5f);
                    }
                }
            } else {
                // No input — fully release brakes so car can roll freely
                currentBrakeForceFront = 0f;
                currentBrakeForceBack  = 0f;
            }
        }

        // Traction Control System (TCS)
        float tcsFactor = 1f;
        for (int i = 0; i < m_backWheels.Count; i++) {
            WheelHit hit;
            if (m_backWheels[i].GetGroundHit(out hit)) {
                float slip = Mathf.Abs(hit.forwardSlip);
                if (slip > 0.4f) {
                    float reduction = 1f - (slip - 0.4f) * 2.5f;
                    if (reduction < 0.2f) reduction = 0.2f;
                    if (reduction < tcsFactor) tcsFactor = reduction;
                }
            }
        }
        motorTorque *= tcsFactor;

        for (int i = 0; i < m_backWheels.Count; i++) {
            m_backWheels[i].motorTorque = motorTorque;
            m_backWheels[i].brakeTorque = currentBrakeForceBack;
        }
        for (int i = 0; i < m_frontWheels.Count; i++) {
            m_frontWheels[i].motorTorque = 0f;
            m_frontWheels[i].brakeTorque = currentBrakeForceFront;
        }
    }

    // ── Anti-Roll ──────────────────────────────────────────────

    private void ApplyAntiRoll() {
        if (m_frontLeftWheel != null && m_frontRightWheel != null)
            ApplyAntiRollForPair(m_frontLeftWheel, m_frontRightWheel);
        if (m_backLeftWheel != null && m_backRightWheel != null)
            ApplyAntiRollForPair(m_backLeftWheel, m_backRightWheel);
    }

    private void ApplyAntiRollForPair(WheelCollider leftW, WheelCollider rightW) {
        WheelHit hit;
        float travelL = 1f;
        float travelR = 1f;

        bool groundedL = leftW.GetGroundHit(out hit);
        if (groundedL)
            travelL = (-leftW.transform.InverseTransformPoint(hit.point).y - leftW.radius) / leftW.suspensionDistance;

        bool groundedR = rightW.GetGroundHit(out hit);
        if (groundedR)
            travelR = (-rightW.transform.InverseTransformPoint(hit.point).y - rightW.radius) / rightW.suspensionDistance;

        float antiRollForceFactor = (travelL - travelR) * antiRollForce;

        if (groundedL) rb.AddForceAtPosition(leftW.transform.up  * -antiRollForceFactor, leftW.transform.position);
        if (groundedR) rb.AddForceAtPosition(rightW.transform.up *  antiRollForceFactor, rightW.transform.position);
    }

    // ── Electronic Stability Control ───────────────────────────

    private void ApplyStabilityControl(Vector3 right, Vector3 up) {
        if (stabilityControl <= 0f) return;

        float lateralVelocity = Vector3.Dot(rb.linearVelocity, right);
        if (Mathf.Abs(lateralVelocity) > 0.1f) {
            float stabilizingTorque = -lateralVelocity * stabilityControl * rb.mass * 0.8f;
            rb.AddRelativeTorque(Vector3.up * stabilizingTorque);
        }

        if (Mathf.Abs(m_horizontalInput) < 0.05f) {
            float yawVel = Vector3.Dot(rb.angularVelocity, up);
            rb.angularVelocity += up * (yawVel * (-0.1f * stabilityControl));
        }
    }

    // ── Downforce ──────────────────────────────────────────────

    private void AddDownforce(Vector3 up) {
        float speed = rb.linearVelocity.magnitude;
        rb.AddForce(-up * downforceFactor * speed);
    }

    // ── Wheel Animation ────────────────────────────────────────

    private void AnimateWheels() {
        for (int i = 0; i < wheels.Count; i++) {
            Quaternion rot;
            Vector3 pos;
            wheels[i].wheelCollider.GetWorldPose(out pos, out rot);
            wheels[i].wheelTransform.position = pos;
            wheels[i].wheelTransform.rotation = rot;
        }
    }

    // ── Suspension & Friction Config ───────────────────────────

    private void ConfigureWheelPhysics() {
        for (int i = 0; i < wheels.Count; i++) {
            WheelCollider wc = wheels[i].wheelCollider;

            JointSpring spring = wc.suspensionSpring;
            float targetMassPerWheel = rb.mass / wheels.Count;
            spring.spring         = targetMassPerWheel * 35f;
            spring.damper         = targetMassPerWheel * 3.5f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring   = spring;

            WheelFrictionCurve sideFriction = wc.sidewaysFriction;
            sideFriction.extremumSlip   = 0.2f;
            sideFriction.extremumValue  = 1.2f;
            sideFriction.asymptoteSlip  = 0.5f;
            sideFriction.asymptoteValue = 0.75f;
            sideFriction.stiffness      = tireGripStiffness;
            wc.sidewaysFriction         = sideFriction;

            WheelFrictionCurve forwardFriction = wc.forwardFriction;
            forwardFriction.extremumSlip   = 0.15f;
            forwardFriction.extremumValue  = 1.0f;
            forwardFriction.asymptoteSlip  = 0.4f;
            forwardFriction.asymptoteValue = 0.5f;
            forwardFriction.stiffness      = 1.5f;
            wc.forwardFriction             = forwardFriction;
        }
    }

    // ── Unity Lifecycle ────────────────────────────────────────

    private void Awake() {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0f, centerOfMassOffset, 0f);
    }

    private void Start() {
        m_invHighSpeedThreshold = 1f / highSpeedThreshold;

        m_frontWheels.Clear();
        m_backWheels.Clear();

        for (int i = 0; i < wheels.Count; i++) {
            if (wheels[i].axel == Axel.Front) {
                m_frontWheels.Add(wheels[i].wheelCollider);
                if (wheels[i].wheelCollider.transform.localPosition.x < 0) m_frontLeftWheel  = wheels[i].wheelCollider;
                else                                                         m_frontRightWheel = wheels[i].wheelCollider;
            } else if (wheels[i].axel == Axel.Back) {
                m_backWheels.Add(wheels[i].wheelCollider);
                if (wheels[i].wheelCollider.transform.localPosition.x < 0) m_backLeftWheel  = wheels[i].wheelCollider;
                else                                                         m_backRightWheel = wheels[i].wheelCollider;
            }
        }

        if (autoConfigureSuspension) {
            ConfigureWheelPhysics();
        }
    }

    private void Update() {
        AnimateWheels();
    }

    private void FixedUpdate() {
        if (rb.isKinematic) return;

        Transform t       = transform;
        Vector3 forward   = t.forward;
        Vector3 right     = t.right;
        Vector3 up        = t.up;

        Steer();
        ApplyDriveAndBrakes(forward);
        ApplyAntiRoll();
        ApplyStabilityControl(right, up);
        AddDownforce(up);
    }
}