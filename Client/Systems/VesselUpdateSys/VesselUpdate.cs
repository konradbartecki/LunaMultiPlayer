﻿using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using LunaClient.Systems.SettingsSys;

namespace LunaClient.Systems.VesselUpdateSys
{
    /// <summary>
    /// This class handle the vessel updates that we received and applies it to the correct vessel. 
    /// It also handle it's interpolations
    /// </summary>
    public class VesselUpdate
    {
        #region Fields

        public Vessel Vessel { get; set; }
        public CelestialBody Body { get; set; }
        public VesselUpdate Target { get; set; }
        public Guid Id { get; set; }
        public int Stage { get; set; }
        public double PlanetTime { get; set; }

        #region Vessel parts information fields

        public uint[] ActiveEngines { get; set; }
        public uint[] StoppedEngines { get; set; }
        public uint[] Decouplers { get; set; }
        public uint[] AnchoredDecouplers { get; set; }
        public uint[] Clamps { get; set; }
        public uint[] Docks { get; set; }

        #endregion

        #region Vessel position information fields

        public Guid VesselId { get; set; }
        public string BodyName { get; set; }
        public float[] Rotation { get; set; }
        public FlightCtrlState FlightState { get; set; }
        public bool[] ActionGrpControls { get; set; }
        public bool IsSurfaceUpdate { get; set; }

        #region Orbit field

        public double[] Orbit { get; set; }

        #endregion

        #region Surface fields

        public double[] Position { get; set; }
        public double[] Velocity { get; set; }
        public double[] Acceleration { get; set; }

        #endregion

        #endregion

        #region Interpolation fields

        public float SentTime { get; set; }
        public bool InterpolationStarted { get; set; }
        public bool InterpolationFinished { get; set; }
        public float ReceiveTime { get; set; }
        public float FinishTime { get; set; }

        #endregion

        #region Private fields

        private float _interpolationDuration;
        private double PlanetariumDifference { get; set; }
        private const float PlanetariumDifferenceLimit = 3f;

        #endregion

        #endregion

        #region Creation methods

        public static VesselUpdate CreateFromVesselId(Guid vesselId)
        {
            var vessel = FlightGlobals.Vessels.FindLast(v => v.id == vesselId);
            return vessel != null ? CreateFromVessel(vessel) : null;
        }

        public static VesselUpdate CreateFromVessel(Vessel vessel)
        {
            try
            {
                var engines = vessel.FindPartModulesImplementing<ModuleEngines>();
                var returnUpdate = new VesselUpdate
                {
                    VesselId = vessel.id,
                    PlanetTime = Planetarium.GetUniversalTime(),
                    ActiveEngines = engines.Where(e => e.EngineIgnited)
                        .Select(e => e.part.craftID).ToArray(),
                    StoppedEngines = engines.Where(e => !e.EngineIgnited)
                        .Select(e => e.part.craftID).ToArray(),
                    Decouplers = vessel.FindPartModulesImplementing<ModuleDecouple>()
                        .Where(e => !e.isDecoupled)
                        .Select(e => e.part.craftID).ToArray(),
                    AnchoredDecouplers = vessel.FindPartModulesImplementing<ModuleAnchoredDecoupler>()
                        .Where(e => !e.isDecoupled)
                        .Select(e => e.part.craftID).ToArray(),
                    Clamps = vessel.FindPartModulesImplementing<LaunchClamp>()
                        .Select(e => e.part.craftID).ToArray(),
                    Docks = vessel.FindPartModulesImplementing<ModuleDockingNode>()
                        .Where(e => !e.IsDisabled)
                        .Select(e => e.part.craftID).ToArray(),
                    Stage = vessel.currentStage,
                    FlightState = new FlightCtrlState(),
                    BodyName = vessel.mainBody.bodyName,
                    Rotation = new[]
                    {
                        vessel.transform.rotation.x,
                        vessel.transform.rotation.y,
                        vessel.transform.rotation.z,
                        vessel.transform.rotation.w
                    },
                    ActionGrpControls = new[]
                    {
                        vessel.ActionGroups[KSPActionGroup.Gear],
                        vessel.ActionGroups[KSPActionGroup.Light],
                        vessel.ActionGroups[KSPActionGroup.Brakes],
                        vessel.ActionGroups[KSPActionGroup.SAS],
                        vessel.ActionGroups[KSPActionGroup.RCS]
                    }
                };

                returnUpdate.FlightState.CopyFrom(vessel.ctrlState);

                if (vessel.altitude < 10000)
                {
                    //Use surface position under 10k
                    returnUpdate.IsSurfaceUpdate = true;

                    returnUpdate.Position = new double[]
                    {
                        vessel.latitude,
                        vessel.longitude,
                        vessel.altitude,
                        vessel.radarAltitude
                    };

                    Vector3d srfVel = Quaternion.Inverse(vessel.mainBody.bodyTransform.rotation) * vessel.srf_velocity;
                    returnUpdate.Velocity = new[]
                    {
                        srfVel.x,
                        srfVel.y,
                        srfVel.z
                    };

                    Vector3d srfAcceleration = Quaternion.Inverse(vessel.mainBody.bodyTransform.rotation) *
                                               vessel.acceleration;
                    returnUpdate.Acceleration = new[]
                    {
                        srfAcceleration.x,
                        srfAcceleration.y,
                        srfAcceleration.z
                    };
                }
                else
                {
                    //Use orbital positioning over 10k
                    returnUpdate.IsSurfaceUpdate = false;

                    returnUpdate.Orbit = new[]
                    {
                        vessel.orbit.inclination,
                        vessel.orbit.eccentricity,
                        vessel.orbit.semiMajorAxis,
                        vessel.orbit.LAN,
                        vessel.orbit.argumentOfPeriapsis,
                        vessel.orbit.meanAnomalyAtEpoch,
                        vessel.orbit.epoch
                    };
                }
                return returnUpdate;
            }
            catch (Exception e)
            {
                Debug.Log($"[LMP]: Failed to get vessel update, exception: {e}");
                return null;
            }
        }

        public VesselUpdate Clone()
        {
            return MemberwiseClone() as VesselUpdate;
        }

        #endregion

        #region Main interpolation method

        /// <summary>
        /// This coroutine is run at every fixed update as we are updating rigid bodies (phisics are involved)
        /// therefore we cannot use it in Update()
        /// </summary>
        /// <returns></returns>
        public IEnumerator ApplyVesselUpdate()
        {
            var fixedUpdate = new WaitForFixedUpdate();
            if (!InterpolationStarted)
            {
                StartupInterpolation();
            }

            if (Body != null && Vessel != null && _interpolationDuration > 0)
            {
                if (SettingsSystem.CurrentSettings.InterpolationEnabled)
                {
                    for (float lerp = 0; lerp < 1; lerp += Time.fixedDeltaTime / _interpolationDuration)
                    {
                        ApplyInterpolations(lerp);
                        yield return fixedUpdate;
                    }
                }
                ApplyInterpolations(1); //we force to apply the last interpolation
                yield return fixedUpdate;
            }

            FinishInterpolation();
        }

        #endregion

        #region Private interpolation methods

        /// <summary>
        /// Finish the interpolation
        /// </summary>
        private void FinishInterpolation()
        {
            try
            {
                InterpolationFinished = true;
                FinishTime = Time.time;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LMP]: Coroutine error in FinishInterpolation {e}");
            }
        }

        /// <summary>
        /// Start the interpolation and set it's needed values
        /// </summary>
        private void StartupInterpolation()
        {
            try
            {
                if (Body == null)
                    Body = FlightGlobals.Bodies.Find(b => b.bodyName == BodyName);
                if (Vessel == null)
                    Vessel = FlightGlobals.Vessels.FindLast(v => v.id == VesselId);

                InterpolationStarted = true;

                if (Body != null && Vessel != null)
                {
                    PlanetariumDifference = Planetarium.GetUniversalTime() - PlanetTime;
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, Target.ActionGrpControls[0]);
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.Light, Target.ActionGrpControls[1]);
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, Target.ActionGrpControls[2]);
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, Target.ActionGrpControls[3]);
                    Vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, Target.ActionGrpControls[4]);

                    var stage = Vessel.currentStage;

                    if (stage != Stage)
                    {
                        Vessel.ActionGroups.ToggleGroup(KSPActionGroup.Stage);
                        Vessel.currentStage = Stage;
                    }
                    else
                    {
                        var engines = Vessel.FindPartModulesImplementing<ModuleEngines>();
                        var enginesToActivate =
                            engines.Where(e => !e.EngineIgnited && ActiveEngines.Contains(e.part.craftID));
                        var enginesToStop =
                            engines.Where(e => e.EngineIgnited && StoppedEngines.Contains(e.part.craftID));

                        var decouplersToLaunch = Vessel.FindPartModulesImplementing<ModuleDecouple>()
                            .Where(d => !d.isDecoupled && !Decouplers.Contains(d.part.craftID));

                        var anchoredDecouplersToLaunch = Vessel.FindPartModulesImplementing<ModuleAnchoredDecoupler>()
                            .Where(d => !d.isDecoupled && !Decouplers.Contains(d.part.craftID));

                        var clamps =
                            Vessel.FindPartModulesImplementing<LaunchClamp>()
                                .Where(c => !Clamps.Contains(c.part.craftID));

                        var docks =
                            Vessel.FindPartModulesImplementing<ModuleDockingNode>()
                                .Where(d => !d.IsDisabled && !Docks.Contains(d.part.craftID));

                        foreach (var engine in enginesToActivate)
                        {
                            engine.Activate();
                        }

                        foreach (var engine in enginesToStop)
                        {
                            engine.Shutdown();
                        }

                        foreach (var decoupler in decouplersToLaunch)
                        {
                            decoupler.Decouple();
                        }

                        foreach (var anchoredDecoupler in anchoredDecouplersToLaunch)
                        {
                            anchoredDecoupler.Decouple();
                        }

                        foreach (var clamp in clamps)
                        {
                            clamp.Release();
                        }

                        foreach (var dock in docks)
                        {
                            dock.Decouple();
                        }
                    }

                    //Here we use the interpolation facor to make the interpolation duration 
                    //shorter or longer depending on the amount of updates we have in queue.
                    //We never exceed the MaxSInterpolationTime
                    _interpolationDuration = Target.SentTime - SentTime - VesselUpdateInterpolationSystem.GetInterpolationFactor(VesselId);
                    _interpolationDuration = Mathf.Clamp(_interpolationDuration, 0, VesselUpdateInterpolationSystem.MaxSInterpolationTime);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LMP]: Coroutine error in StartupInterpolation {e}");
            }
        }

        /// <summary>
        /// Apply the interpolation based on a percentage
        /// </summary>
        private void ApplyInterpolations(float percentage)
        {
            try
            {
                if (Vessel == null || percentage > 1) return;

                ApplyRotationInterpolation(percentage);

                if (IsSurfaceUpdate)
                    ApplySurfaceInterpolation(percentage);
                else
                    ApplyOrbitInterpolation(percentage);

                ApplyControlState(percentage);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LMP]: Coroutine error in ApplyInterpolations {e}");
            }
        }

        /// <summary>
        /// Applies the control state
        /// </summary>
        private void ApplyControlState(float percentage)
        {
            var currentFlightState = Lerp(FlightState, Target.FlightState, percentage);

            Vessel?.ctrlState.CopyFrom(currentFlightState);
            Vessel?.FeedInputFeed();

            if (VesselCommon.IsSpectating)
            {
                FlightInputHandler.state.CopyFrom(currentFlightState);
            }
        }

        /// <summary>
        /// Applies common interpolation values
        /// </summary>
        private void ApplyRotationInterpolation(float interpolationValue)
        {
            var startRot = new Quaternion(Rotation[0], Rotation[1], Rotation[2], Rotation[3]);
            var targetRot = new Quaternion(Target.Rotation[0], Target.Rotation[1], Target.Rotation[2], Target.Rotation[3]);

            var currentRot = Quaternion.Slerp(startRot, targetRot, interpolationValue);

            Vessel.SetRotation(currentRot);
        }

        /// <summary>
        /// Applies surface interpolation values
        /// </summary>
        private void ApplySurfaceInterpolation(float interpolationValue)
        {
            var currentAcc = GetInterpolatedAcceleration(interpolationValue);
            var currentVelocity = GetInterpolatedVelocity(interpolationValue, currentAcc);

            //Use the average velocity to determine the new position
            //Displacement = v0*t + 1/2at^2.
            //var positionFudge = (currentVelocity*PlanetariumDifference) + (0.5d*currentAcc*PlanetariumDifference*PlanetariumDifference);
            //return Vector3d.Lerp(startPos + positionFudge, targetPos + positionFudge, interpolationValue);

            Vessel.latitude = Lerp(Position[0], Target.Position[0], interpolationValue);
            Vessel.longitude = Lerp(Position[1], Target.Position[1], interpolationValue);
            Vessel.altitude = Lerp(Position[2], Target.Position[2], interpolationValue);
            Vessel.radarAltitude = Lerp(Position[3], Target.Position[3], interpolationValue);

            Vessel.ChangeWorldVelocity(currentVelocity - Vessel.srf_velocity);
            Vessel.acceleration = currentAcc;
        }

        /// <summary>
        /// Here we get the interpolated velocity. 
        /// We should fudge it as we are seeing the client IN THE PAST so we need to extrapolate the speed 
        /// </summary>
        private Vector3d GetInterpolatedVelocity(float interpolationValue, Vector3d acceleration)
        {
            var startVel = new Vector3d(Velocity[0], Velocity[1], Velocity[2]);
            var targetVel = new Vector3d(Target.Velocity[0], Target.Velocity[1], Target.Velocity[2]);

            //Velocity = a*t
            //var velocityFudge = acceleration*PlanetariumDifference;

            return Body.bodyTransform.rotation * Vector3d.Lerp(startVel, targetVel, interpolationValue);
            //return Body.bodyTransform.rotation*Vector3d.Lerp(startVel + velocityFudge, targetVel + velocityFudge, interpolationValue);
        }

        /// <summary>
        /// Here we get the interpolated acceleration
        /// </summary>
        private Vector3d GetInterpolatedAcceleration(float interpolationValue)
        {
            var startAcc = new Vector3d(Acceleration[0], Acceleration[1], Acceleration[2]);
            var targetAcc = new Vector3d(Target.Acceleration[0], Target.Acceleration[1], Target.Acceleration[2]);
            Vector3d currentAcc = Body.bodyTransform.rotation * Vector3d.Lerp(startAcc, targetAcc, interpolationValue);
            return currentAcc;
        }

        /// <summary>
        /// Applies interpolation when above 10000m
        /// </summary>
        private void ApplyOrbitInterpolation(float interpolationValue)
        {
            var startOrbit = new Orbit(Orbit[0], Orbit[1], Orbit[2], Orbit[3], Orbit[4], Orbit[5], Orbit[6], Body);
            var targetOrbit = new Orbit(Target.Orbit[0], Target.Orbit[1], Target.Orbit[2], Target.Orbit[3], Target.Orbit[4],
                Target.Orbit[5], Target.Orbit[6], Body);

            var currentOrbit = OrbitLerp(startOrbit, targetOrbit, Body, interpolationValue);

            currentOrbit.Init();
            currentOrbit.UpdateFromUT(Planetarium.GetUniversalTime());

            var latitude = Body.GetLatitude(currentOrbit.pos);
            var longitude = Body.GetLongitude(currentOrbit.pos);
            var altitude = Body.GetAltitude(currentOrbit.pos);

            Vessel.latitude = latitude;
            Vessel.longitude = longitude;
            Vessel.altitude = altitude;
            Vessel.protoVessel.latitude = latitude;
            Vessel.protoVessel.longitude = longitude;
            Vessel.protoVessel.altitude = altitude;

            if (Vessel.packed)
            {
                //The OrbitDriver update call will set the vessel position on the next fixed update
                CopyOrbit(currentOrbit, Vessel.orbitDriver.orbit);
                Vessel.orbitDriver.pos = Vessel.orbitDriver.orbit.pos.xzy;
                Vessel.orbitDriver.vel = Vessel.orbitDriver.orbit.vel;
            }
            else
            {
                var posDelta = currentOrbit.getPositionAtUT(Planetarium.GetUniversalTime()) -
                               Vessel.orbitDriver.orbit.getPositionAtUT(Planetarium.GetUniversalTime());

                var velDelta = currentOrbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy -
                               Vessel.orbitDriver.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy;

                Vessel.Translate(posDelta);
                Vessel.ChangeWorldVelocity(velDelta);
            }
        }

        //Credit where credit is due, Thanks hyperedit.
        private static void CopyOrbit(Orbit sourceOrbit, Orbit destinationOrbit)
        {
            destinationOrbit.inclination = sourceOrbit.inclination;
            destinationOrbit.eccentricity = sourceOrbit.eccentricity;
            destinationOrbit.semiMajorAxis = sourceOrbit.semiMajorAxis;
            destinationOrbit.LAN = sourceOrbit.LAN;
            destinationOrbit.argumentOfPeriapsis = sourceOrbit.argumentOfPeriapsis;
            destinationOrbit.meanAnomalyAtEpoch = sourceOrbit.meanAnomalyAtEpoch;
            destinationOrbit.epoch = sourceOrbit.epoch;
            destinationOrbit.referenceBody = sourceOrbit.referenceBody;
            destinationOrbit.Init();
            destinationOrbit.UpdateFromUT(Planetarium.GetUniversalTime());
        }

        /// <summary>
        /// Custom lerp for orbits
        /// </summary>
        private static Orbit OrbitLerp(Orbit startOrbit, Orbit endOrbit, CelestialBody body, float interpolationValue)
        {
            var inc = Lerp(startOrbit.inclination, endOrbit.inclination, interpolationValue);
            var e = Lerp(startOrbit.eccentricity, endOrbit.eccentricity, interpolationValue);
            var sma = Lerp(startOrbit.semiMajorAxis, endOrbit.semiMajorAxis, interpolationValue);
            var lan = Lerp(startOrbit.LAN, endOrbit.LAN, interpolationValue);
            var argPe = Lerp(startOrbit.argumentOfPeriapsis, endOrbit.argumentOfPeriapsis, interpolationValue);
            var mEp = Lerp(startOrbit.meanAnomalyAtEpoch, endOrbit.meanAnomalyAtEpoch, interpolationValue);
            var t = Lerp(startOrbit.epoch, endOrbit.epoch, interpolationValue);

            var orbitLerp = new Orbit(inc, e, sma, lan, argPe, mEp, t, body);
            return orbitLerp;
        }

        /// <summary>
        /// Custom lerp as Unity does not have a lerp for double values
        /// </summary>
        private static double Lerp(double from, double to, float t)
        {
            return from * (1 - t) + to * t;
        }

        /// <summary>
        /// Custom lerp for a flight control state
        /// </summary>
        private static FlightCtrlState Lerp(FlightCtrlState from, FlightCtrlState to, float t)
        {
            return new FlightCtrlState
            {
                X = Mathf.Lerp(from.X, to.X, t),
                Y = Mathf.Lerp(from.Y, to.Y, t),
                Z = Mathf.Lerp(from.Z, to.Z, t),
                gearDown = t < 0.5 ? from.gearDown : to.gearDown,
                gearUp = t < 0.5 ? from.gearUp : to.gearUp,
                headlight = t < 0.5 ? from.headlight : to.headlight,
                killRot = t < 0.5 ? from.killRot : to.killRot,
                mainThrottle = Mathf.Lerp(from.mainThrottle, to.mainThrottle, t),
                pitch = Mathf.Lerp(from.pitch, to.pitch, t),
                roll = Mathf.Lerp(from.roll, to.roll, t),
                yaw = Mathf.Lerp(from.yaw, to.yaw, t),
                pitchTrim = Mathf.Lerp(from.pitchTrim, to.pitchTrim, t),
                rollTrim = Mathf.Lerp(from.rollTrim, to.rollTrim, t),
                yawTrim = Mathf.Lerp(from.yawTrim, to.yawTrim, t),
                wheelSteer = Mathf.Lerp(from.wheelSteer, to.wheelSteer, t),
                wheelSteerTrim = Mathf.Lerp(from.wheelSteerTrim, to.wheelSteerTrim, t),
                wheelThrottle = Mathf.Lerp(from.wheelThrottle, to.wheelThrottle, t),
                wheelThrottleTrim = Mathf.Lerp(from.wheelThrottleTrim, to.wheelThrottleTrim, t),
            };
        }

        #endregion
    }
}