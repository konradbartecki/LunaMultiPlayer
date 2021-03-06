﻿using System;
using LunaCommon.Enums;
using LunaCommon.Message.Base;
using LunaCommon.Message.Types;

namespace LunaCommon.Message.Data.Chat
{
    public class ChatBaseMsgData : MessageData
    {
        public override ushort SubType => (ushort)(int)ChatMessageType;

        public virtual ChatMessageType ChatMessageType
        {
            get { throw new NotImplementedException(); }
        }

        public string From { get; set; }
    }
}