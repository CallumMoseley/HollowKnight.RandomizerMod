﻿namespace MultiWorldProtocol.Messaging
{
    public enum MWMessageType
    {
        InvalidMessage=0,
        SharedCore=1,
        ConnectMessage,
        ReconnectMessage,
        DisconnectMessage,
        JoinMessage,
        JoinConfirmMessage,
        LeaveMessage,
        ItemReceiveMessage,
        ItemReceiveConfirmMessage,
        ItemSendMessage,
        ItemSendConfirmMessage,
        NotifyMessage,
        ReadyConfirmMessage,
        PingMessage,
        ReadyMessage,
        RejoinMessage,
        ResultMessage,
        SaveMessage,
        SetupMessage,
        StartMessage,
        UnreadyMessage
    }
}
