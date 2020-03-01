using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public class Connection
    {
        public bool IsConnected { get; internal set; } = false;
        int m_BytesToReceive = 0;
        int m_InitialBufferCount = 0;
        public int ConnectionID { get; private set; }
        internal Socket Socket { get; set; }

        internal byte[] SizeReadBuffer = new byte[2];
        internal SocketArgs SocketArg { get; private set; }
        internal SocketArgs DisconnectArg { get; private set; }
        internal CountdownEvent DisconnectEvent = new CountdownEvent(0);
        internal Peer Peer;

        internal Connection(int id, Peer peer)
        {
            Peer = peer;
            ConnectionID = id;
            SocketArg = new SocketArgs();
            SocketArg.BufferList = new List<ArraySegment<byte>>();
            SocketArg.Connection = this;
            DisconnectArg = new SocketArgs();
            DisconnectArg.Connection = this;
            DisconnectArg.DisconnectReuseSocket = true;
            Socket = CreateSocket(Peer.NoDelay, Peer.KeepAlive, 30000, 5000);
            ReadyToReceiveLength();
        }

        internal bool TryReleaseMessage(out Message message)
        {
            if (SocketArg.Message != null)
            {
                //true means we have received all bytes for message
                if (AppendReceivedBuffer())
                {
                    message = SocketArg.Message;
                    SocketArg.Message = null;
                    ReadyToReceiveLength();
                    return true;
                }
            }
            else
            {
                //true means we have received all bytes for length
                if (HandleLengthReceive())
                {
                    //now prepare a message to receive actual data
                    SocketArg.Message = Message.Pop();
                    ReadyToReceiveMessage();
                }
            }

            message = null;
            return false;
        }

        internal void ReadyToReceiveLength()
        {
            SocketArg.BufferList.Clear();
            SocketArg.BufferList.Add(new ArraySegment<byte>(SizeReadBuffer));
            SocketArg.BufferList = SocketArg.BufferList;
            m_BytesToReceive = SizeReadBuffer.Length;
        }

        internal bool HandleLengthReceive()
        {
            m_BytesToReceive -= SocketArg.BytesTransferred;
            if (m_BytesToReceive == 0)
            {
                //now we have size howmuch we can receive, set it
                m_BytesToReceive = MessageReader.ReadUInt16(SizeReadBuffer);
                return true;
            }
            //this is rare case, only one byte is received due to packet drop, we have to wait for another byte
            SocketArg.BufferList[0] = new ArraySegment<byte>(SizeReadBuffer, SizeReadBuffer.Length - m_BytesToReceive, m_BytesToReceive);
            SocketArg.BufferList = SocketArg.BufferList;
            return false;
        }

        internal void ReadyToReceiveMessage()
        {
            SocketArg.Message.BindToArgsReceive(SocketArg, m_BytesToReceive);
            SocketArg.Message.Size = m_BytesToReceive;
            m_InitialBufferCount = SocketArg.BufferList.Count;
        }

        private bool AppendReceivedBuffer()
        {
            m_BytesToReceive -= SocketArg.BytesTransferred;
            //received properly
            if (m_BytesToReceive == 0) return true;
            var bytesReceived = SocketArg.Message.Size - m_BytesToReceive;
            Message.AdvanceRecevedOffset(SocketArg, m_InitialBufferCount, bytesReceived);
            return false;
        }

        internal void ClearReceiving()
        {
            SocketArg.ClearMessage();
            ReadyToReceiveLength();
        }

        internal void RebuildSocket()
        {
            Socket = CreateSocket(Peer.NoDelay, Peer.KeepAlive, 30000, 5000);
        }

        protected static Socket CreateSocket(bool noDelay, bool keepAlive, uint interval, uint retryInterval)
        {
            //create new socket
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

            //default settings
            socket.NoDelay = noDelay;
            socket.SendBufferSize = 512;
            socket.ReceiveBufferSize = 512;
            socket.DualMode = true;

            //linger for reuse socket
            socket.LingerState = new LingerOption(true, 0);

            //keep alive setting
            if(keepAlive)
            {
                int size = System.Runtime.InteropServices.Marshal.SizeOf(new uint());
                var inOptionValues = new byte[size * 3];
                BitConverter.GetBytes((uint)(keepAlive ? 1 : 0)).CopyTo(inOptionValues, 0);
                BitConverter.GetBytes(interval).CopyTo(inOptionValues, size);
                BitConverter.GetBytes(retryInterval).CopyTo(inOptionValues, size * 2);
                socket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
            }

            return socket;
        }

    }
}
    
