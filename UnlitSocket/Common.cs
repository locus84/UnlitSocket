﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UnlitSocket
{
    public enum ConnectionStatus { Disconnected, Connecting, Connected }
    public enum MessageType { Connected, Disconnected, Data }

    public interface IMessageHandler
    {
        void OnConnected(int connectionId);
        void OnDisconnected(int connectionId);
        void OnDataReceived(int connectionId, Message msg);
    }

    public interface ILogReceiver
    {
        void Debug(string msg);
        void Warning(string msg);
        void Exception(Exception exception);
    }

    public struct KeepAliveOption
    {
        public bool Enabled;
        public int Time;
        public int Interval;
        public KeepAliveOption(bool enabled, int time, int interval)
        {
            Enabled = enabled;
            Time = time;
            Interval = interval;
        }
    }

    public struct ReceivedMessage
    {
        public int ConnectionId;
        public MessageType Type;
        public Message MessageData;
        public ReceivedMessage(int connectionId, MessageType type, Message message = null)
        {
            ConnectionId = connectionId;
            Type = type;
            MessageData = message;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntFloat
    {
        [FieldOffset(0)]
        public float floatValue;

        [FieldOffset(0)]
        public uint intValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDouble
    {
        [FieldOffset(0)]
        public double doubleValue;

        [FieldOffset(0)]
        public ulong longValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDecimal
    {
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public decimal decimalValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntGuid
    {
        [FieldOffset(0)] public uint A;
        [FieldOffset(4)] public ushort B;
        [FieldOffset(6)] public ushort C;
        [FieldOffset(8)] public byte D;
        [FieldOffset(9)] public byte E;
        [FieldOffset(10)] public byte F;
        [FieldOffset(11)] public byte G;
        [FieldOffset(12)] public byte H;
        [FieldOffset(13)] public byte I;
        [FieldOffset(14)] public byte J;
        [FieldOffset(15)] public byte K;

        [FieldOffset(0)]
        public Guid guidValue;
    }

    internal class SocketArgs : SocketAsyncEventArgs
    {
        public Message Message;
        public Connection Connection;

        public void ClearMessage()
        {
            if (Message != null)
            {
                Message.Release();
                Message = null;
            }
        }
    }

    internal class ThreadSafeQueue<T>
    {
        Queue<T> m_InnerQueue = new Queue<T>();

        public void Enqueue(T item)
        {
            lock (m_InnerQueue)
            {
                m_InnerQueue.Enqueue(item);
            }
        }

        public bool TryDequeue(out T result)
        {
            lock (m_InnerQueue)
            {
                result = default(T);
                if (m_InnerQueue.Count > 0)
                {
                    result = m_InnerQueue.Dequeue();
                    return true;
                }
                return false;
            }
        }

        public void DequeueAll(List<T> list)
        {
            lock (m_InnerQueue)
            {
                list.AddRange(m_InnerQueue);
                m_InnerQueue.Clear();
            }
        }
    }
}

