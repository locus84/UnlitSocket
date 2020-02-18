using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class UserToken
    {
        internal IConnection Connection;
        public bool IsConnected { get; internal set; } = false;
        int m_ReadTotal = 0;
        int m_SizeTotal = 0;
        int m_InitialBufferCount = 0;
        public int ConnectionID { get; private set; }
        internal Socket Socket { get; set; }
        internal int LastTransferCount;

        internal byte[] SizeReadBuffer = new byte[2];
        internal SocketAsyncEventArgs ReceiveArg { get; private set; }
        internal Message CurrentMessage = null;
        internal Peer Peer;

        internal void ReadyToReceiveLength()
        {
            ReceiveArg.BufferList.Clear();
            ReceiveArg.BufferList.Add(new ArraySegment<byte>(SizeReadBuffer));
            ReceiveArg.BufferList = ReceiveArg.BufferList;
            m_ReadTotal = 0;
        }

        internal bool HandleLengthReceive(int byteTransferred)
        {
            if (m_ReadTotal + byteTransferred == SizeReadBuffer.Length)
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

        internal void ReadyToReceiveMessage()
        {
            CurrentMessage.BindToArgsReceive(ReceiveArg, m_SizeTotal);
            m_ReadTotal = 0;
            m_InitialBufferCount = ReceiveArg.BufferList.Count;
        }

        internal bool AppendReceivedBuffer(int receiveCount)
        {
            m_ReadTotal += receiveCount;
            //received properly
            if (m_ReadTotal == m_SizeTotal) return true;
            Message.AdvanceRecevedOffset(ReceiveArg, m_InitialBufferCount, m_ReadTotal);
            return false;
        }

        internal UserToken(int id, Peer peer, IConnection connection)
        {
            Peer = peer;
            ConnectionID = id;
            Connection = connection;
            Connection.UserToken = this;
            ReceiveArg = new SocketAsyncEventArgs();
            ReceiveArg.BufferList = new List<ArraySegment<byte>>();
            ReceiveArg.UserToken = this;

            Socket = CreateSocket(true, 30000, 5000);
        }

        internal void RebuildSocket()
        {
            Socket = CreateSocket(true, 30000, 5000);
        }

        protected static Socket CreateSocket(bool keepAlive, uint interval, uint retryInterval)
        {
            //create new socket
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

            //default settings
            socket.SendTimeout = 5000;
            socket.NoDelay = true;
            socket.Blocking = false;
            socket.SendBufferSize = 512;
            socket.ReceiveBufferSize = 512;
            socket.SendTimeout = 5000;
            socket.DualMode = true;

            //linger for reuse socket
            socket.LingerState = new LingerOption(false, 0);

            //keep alive setting
            int size = System.Runtime.InteropServices.Marshal.SizeOf(new uint());
            var inOptionValues = new byte[size * 3];
            BitConverter.GetBytes((uint)(keepAlive ? 1 : 0)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes(interval).CopyTo(inOptionValues, size);
            BitConverter.GetBytes(retryInterval).CopyTo(inOptionValues, size * 2);
            socket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);

            return socket;
        }
    }
}
    
