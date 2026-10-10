using UnityEngine;

namespace Wise.Skyfarer;

public class LandingGear : MonoBehaviour
{
    public WheelCollider[] Wheels;
    public Transform[] Visuals;
    public const float Radius = .72f;
    private static readonly int SteeringKey = "skyfarer.wheelSteering".GetStableHashCode();
    private Rigidbody body;
    private ZNetView view;
    private float remoteSpin;
    internal int GroundedCount { get; private set; }
    internal Vector3 GroundNormal { get; private set; } = Vector3.up;
    internal float SpinDegrees { get; private set; }
    internal bool Braking { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        view = GetComponent<ZNetView>();
        foreach (var wheel in Wheels) wheel.ConfigureVehicleSubsteps(8, 5, 3);
    }

    internal void Step(float steering, float throttle, bool wheelBrake, bool reverse = false)
    {
        GroundedCount = 0;
        Vector3 normal = Vector3.zero;
        float speed = body.linearVelocity.magnitude;
        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        float drive = Mathf.Clamp(((reverse ? -4f : throttle * SkyfarerSettings.TopSpeed) - forwardSpeed) * .7f, -5, 5);
        float angle = steering * Mathf.Lerp(28, 12, Mathf.InverseLerp(2, 18, speed));
        Braking = wheelBrake || (throttle < .03f && !reverse);
        view.GetZDO().Set(SteeringKey, angle);
        for (int i = 0; i < Wheels.Length; i++)
        {
            var wheel = Wheels[i];
            wheel.steerAngle = i < 2 ? angle : 0;
            wheel.motorTorque = Braking || !wheel.isGrounded ? 0 : drive * body.mass * Radius / Wheels.Length;
            wheel.brakeTorque = Braking ? 900 : 0;
            if (wheel.GetGroundHit(out var hit))
            {
                GroundedCount++;
                normal += hit.normal;
            }
        }
        GroundNormal = GroundedCount > 0 ? normal.normalized : Vector3.up;
    }

    private void FixedUpdate()
    {
        bool owner = view && view.IsValid() && view.IsOwner();
        foreach (var wheel in Wheels)
            if (wheel.enabled != owner) wheel.enabled = owner;
    }

    private void LateUpdate()
    {
        if (!view || !view.IsValid()) return;
        bool owner = view.IsOwner();
        float steer = view.GetZDO().GetFloat(SteeringKey);
        float spinStep = Vector3.Dot(body.linearVelocity, transform.forward) / Radius * Mathf.Rad2Deg * Time.deltaTime;
        remoteSpin = Mathf.Repeat(remoteSpin + spinStep, 360);
        for (int i = 0; i < Wheels.Length; i++)
        {
            if (owner && Wheels[i].enabled)
            {
                Wheels[i].GetWorldPose(out var position, out var rotation);
                Visuals[i].SetPositionAndRotation(position, rotation);
                if (i == 0) SpinDegrees += Mathf.Abs(Wheels[i].rpm * 6 * Time.deltaTime);
            }
            else
            {
                Visuals[i].position = Wheels[i].transform.position - transform.up * .16f;
                Visuals[i].rotation = transform.rotation * Quaternion.Euler(0, i < 2 ? steer : 0, 0) * Quaternion.Euler(remoteSpin, 0, 0);
            }
        }
    }
}
