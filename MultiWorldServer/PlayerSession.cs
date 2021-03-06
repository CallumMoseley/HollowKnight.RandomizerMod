﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MultiWorldProtocol.Messaging;
using MultiWorldProtocol.Messaging.Definitions.Messages;

namespace MultiWorldServer
{
    class PlayerSession
    {
        public string Name;
        public int randoId;
        public int playerId;
        public ulong uid;

        public readonly List<ResendEntry> MessagesToConfirm = new List<ResendEntry>();

        public PlayerSession(string Name, int randoId, int playerId, ulong uid)
        {
            this.Name = Name;
            this.randoId = randoId;
            this.playerId = playerId;
            this.uid = uid;
        }

        public void QueueConfirmableMessage(MWMessage message)
        {
            if (message.MessageType != MWMessageType.ItemReceiveMessage)
            {
                throw new InvalidOperationException("Server should only queue ItemReceive messages for confirmation");
            }
            lock (MessagesToConfirm)
            {
                MessagesToConfirm.Add(new ResendEntry(message));
            }
        }

        public List<MWMessage> ConfirmMessage(MWMessage message)
        {
            if (message.MessageType == MWMessageType.ItemReceiveConfirmMessage)
            {
                return ConfirmItemReceive((MWItemReceiveConfirmMessage)message);
            }
            else
            {
                throw new InvalidOperationException("Must only confirm ItemReceive messages.");
            }
        }

        private List<MWMessage> ConfirmItemReceive(MWItemReceiveConfirmMessage message)
        {
            List<MWMessage> confirmedMessages = new List<MWMessage>();

            lock (MessagesToConfirm)
            {
                for (int i = MessagesToConfirm.Count - 1; i >= 0; i--)
                {
                    MWItemReceiveMessage icm = MessagesToConfirm[i].Message as MWItemReceiveMessage;
                    if (icm.Item == message.Item && icm.From == message.From)
                    {
                        confirmedMessages.Add(icm);
                        MessagesToConfirm.RemoveAt(i);
                    }
                }
            }

            return confirmedMessages;
        }
    }
}
