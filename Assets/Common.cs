using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UnlitSocket
{
    public delegate void ConnectionStatusChangeDelegate(int connectionID);
    public delegate void DataReceivedDelegate(int connectionID, Message message);

    public enum ConnectionStatus { Disconnected, Connecting, Connected }
    public enum MessageType { Connected, Disconnected, Data }

    public interface ILogReceiver
    {
        void Debug(string str);
        void Exception(string exception);
    }

    public class AsyncUserToken
    {
        public bool IsConnected = false;
        int m_ReadTotal = 0;
        int m_SizeTotal = 0;
        int m_InitialBufferCount = 0;
        public int ConnectionID { get; private set; }
        public Socket Socket { get; set; }

        public byte[] SizeReadBuffer = new byte[2];
        public SocketAsyncEventArgs ReceiveArg { get; private set; }
        public Message CurrentMessage = null;

        public void ReadyToReceiveLength()
        {
            ReceiveArg.BufferList.Clear();
            ReceiveArg.BufferList.Add(new ArraySegment<byte>(SizeReadBuffer));
            ReceiveArg.BufferList = ReceiveArg.BufferList;
            m_ReadTotal = 0;
        }

        public bool HandleLengthReceive(int byteTransferred)
        {
            if(m_ReadTotal + byteTransferred == SizeReadBuffer.Length)
            {
                //now we have size howmuch we can receive, set it
                m_SizeTotal = MessageReader.ReadUInt16(SizeReadBuffer);
                return true;
            }
            //this is rare case, only one byte is received due to packet drop, we have to wait for another byte
            m_ReadTotal += byteTransferred;
            ReceiveArg.BufferList[0] = new ArraySegment<byte>(SizeReadBuffer, m_ReadTotal, SizeReadBuffer.Length - m_ReadTotal);
            ReceiveArg.BufferList = ReceiveArg.BufferList;
            return false;
        }

        public void ReadyToReceiveMessage()
        {
            CurrentMessage.BindToArgsReceive(ReceiveArg, m_SizeTotal);
            m_ReadTotal = 0;
            m_InitialBufferCount = ReceiveArg.BufferList.Count;
        }

        public bool AppendReceivedBuffer(int receiveCount)
        {
            m_ReadTotal += receiveCount;
            //received properly
            if (m_ReadTotal == m_SizeTotal) return true;
            Message.AdvanceRecevedOffset(ReceiveArg, m_InitialBufferCount, m_ReadTotal);
            return false;
        }

        public AsyncUserToken(int id)
        {
            ConnectionID = id;
            ReceiveArg = new SocketAsyncEventArgs();
            ReceiveArg.BufferList = new List<ArraySegment<byte>>();
            ReceiveArg.UserToken = this;
        }
    }

    internal class AsyncUserTokenPool
    {
        Stack<AsyncUserToken> m_pool;

        public AsyncUserTokenPool(int capacity)
        {
            m_pool = new Stack<AsyncUserToken>(capacity);
        }

        public void Push(AsyncUserToken item)
        {
            if (item == null) { throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null"); }
            lock (m_pool)
            {
                m_pool.Push(item);
            }
        }

        public AsyncUserToken Pop()
        {
            lock (m_pool)
            {
                return m_pool.Pop();
            }
        }

        public int Count
        {
            get { return m_pool.Count; }
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
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public Guid guidValue;
    }
}

