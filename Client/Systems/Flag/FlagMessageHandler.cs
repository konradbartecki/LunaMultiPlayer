﻿using System;
using System.Collections.Concurrent;
using System.IO;
using LunaClient.Base;
using LunaClient.Base.Interface;
using LunaClient.Utilities;
using LunaCommon;
using LunaCommon.Message.Data.Flag;
using LunaCommon.Message.Interface;
using LunaCommon.Message.Types;
using UnityEngine;

namespace LunaClient.Systems.Flag
{
    public class FlagMessageHandler : SubSystem<FlagSystem>, IMessageHandler
    {
        public ConcurrentQueue<IMessageData> IncomingMessages { get; set; } = new ConcurrentQueue<IMessageData>();

        public void HandleMessage(IMessageData messageData)
        {
            var msgData = messageData as FlagBaseMsgData;
            if (msgData == null) return;

            switch (msgData.FlagMessageType)
            {
                case FlagMessageType.LIST:
                {
                    var data = (FlagListMsgData) messageData;
                    var serverFlagFiles = data.FlagFileNames;
                    var serverFlagOwners = data.FlagOwners;
                    var serverFlagShaSums = data.FlagShaSums;
                    for (var i = 0; i < serverFlagFiles.Length; i++)
                    {
                        var fi = new FlagInfo
                        {
                            Owner = serverFlagOwners[i],
                            ShaSum = serverFlagShaSums[i]
                        };
                        System.ServerFlags[Path.GetFileNameWithoutExtension(serverFlagFiles[i])] = fi;
                    }
                    System.SyncComplete = true;
                    //Check if we need to upload the flag
                    System.FlagChangeEvent = true;
                }
                    break;
                case FlagMessageType.FLAG_DATA:
                {
                    var data = (FlagDataMsgData) messageData;
                    var frm = new FlagRespondMessage
                    {
                        FlagInfo = {Owner = data.OwnerPlayerName},
                        FlagName = data.FlagName,
                        FlagData = data.FlagData
                    };
                    frm.FlagInfo.ShaSum = Common.CalculateSha256Hash(frm.FlagData);
                    System.NewFlags.Enqueue(frm);
                }
                    break;
                case FlagMessageType.DELETE_FILE:
                {
                    var data = (FlagDeleteMsgData) messageData;
                    var flagName = data.FlagName;
                    var flagFile = CommonUtil.CombinePaths(System.FlagPath, flagName);
                    DeleteFlag(flagFile);
                }
                    break;
            }
        }

        private static void DeleteFlag(string flagFile)
        {
            try
            {
                if (File.Exists(flagFile))
                {
                    Debug.Log($"[LMP]: Deleting flag {flagFile}");
                    File.Delete(flagFile);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LMP]: Error deleting flag {flagFile}, exception: {e}");
            }
        }
    }
}