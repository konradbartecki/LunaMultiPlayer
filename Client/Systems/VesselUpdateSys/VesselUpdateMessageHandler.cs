﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using LunaClient.Base;
using LunaClient.Base.Interface;
using LunaCommon.Message.Data.Vessel;
using LunaCommon.Message.Interface;
using UnityEngine;

namespace LunaClient.Systems.VesselUpdateSys
{
    public class VesselUpdateMessageHandler : SubSystem<VesselUpdateSystem>, IMessageHandler
    {
        public ConcurrentQueue<IMessageData> IncomingMessages { get; set; } = new ConcurrentQueue<IMessageData>();

        public void HandleMessage(IMessageData messageData)
        {
            var msgData = messageData as VesselUpdateMsgData;

            if (msgData == null || !System.UpdateSystemBasicReady || VesselCommon.UpdateIsForOwnVessel(msgData.VesselId))
            {
                return;
            }

            var update = new VesselUpdate
            {
                Id = Guid.NewGuid(),
                ReceiveTime = Time.fixedTime,
                PlanetTime = msgData.PlanetTime,
                Stage = msgData.Stage,
                SentTime = msgData.GameSentTime,
                ActiveEngines = msgData.ActiveEngines,
                StoppedEngines = msgData.StoppedEngines,
                Decouplers = msgData.Decouplers,
                AnchoredDecouplers = msgData.AnchoredDecouplers,
                Clamps = msgData.Clamps,
                Docks = msgData.Docks,
                VesselId = msgData.VesselId,
                BodyName = msgData.BodyName,
                Rotation = msgData.Rotation,
                FlightState = new FlightCtrlState
                {
                    mainThrottle = msgData.MainThrottle,
                    wheelThrottleTrim = msgData.WheelThrottleTrim,
                    X = msgData.X,
                    Y = msgData.Y,
                    Z = msgData.Z,
                    killRot = msgData.KillRot,
                    gearUp = msgData.GearUp,
                    gearDown = msgData.GearDown,
                    headlight = msgData.Headlight,
                    wheelThrottle = msgData.WheelThrottle,
                    roll = msgData.Roll,
                    yaw = msgData.Yaw,
                    pitch = msgData.Pitch,
                    rollTrim = msgData.RollTrim,
                    yawTrim = msgData.YawTrim,
                    pitchTrim = msgData.PitchTrim,
                    wheelSteer = msgData.WheelSteer,
                    wheelSteerTrim = msgData.WheelSteerTrim
                },
                ActionGrpControls = msgData.ActiongroupControls,
                IsSurfaceUpdate = msgData.IsSurfaceUpdate
            };

            if (update.IsSurfaceUpdate)
            {
                update.Position = msgData.Position;
                update.Velocity = msgData.Velocity;
                update.Acceleration = msgData.Acceleration;
            }
            else
            {
                update.Orbit = msgData.Orbit;
            }

            if (!System.ReceivedUpdates.ContainsKey(update.VesselId))
            {
                System.ReceivedUpdates.Add(update.VesselId, new Queue<VesselUpdate>());
            }

            if (System.ReceivedUpdates[update.VesselId].Count + 1 > VesselUpdateInterpolationSystem.MaxTotalUpdatesInQueue)
                System.ReceivedUpdates[update.VesselId].Dequeue();

            System.ReceivedUpdates[update.VesselId].Enqueue(update);
        }
    }
}
