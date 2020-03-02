using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public class Connection
    {
        internal int ConnectionID { get; private set; }

        internal Socket Socket { get; set; }
        internal SocketArgs ReceiveArg { get; private set; }
        internal SocketArgs DisconnectArg { get; private set; }
        internal CountdownEvent DisconnectEvent = new CountdownEvent(0);

        internal bool IsConnected { get => m_ConnectedInt > 0;  }
        int m_ConnectedInt = 0;
        int m_BytesToReceive = 0;
        int m_InitialBufferCount = 0;
        byte[] m_SizeReadBuffer = new byte[2];
        byte[] m_KeepAliveOptionCache = new byte[12];

        internal Connection(int id)
        {
            ConnectionID = id;
            ReceiveArg = new SocketArgs();
            ReceiveArg.BufferList = new List<ArraySegment<byte>>();
            ReceiveArg.Connection = this;
            DisconnectArg = new SocketArgs();
            DisconnectArg.Connection = this;
            DisconnectArg.DisconnectReuseSocket = true;
            PrepareReceiveLength();
        }

        internal void SetConnectedAndResetEvent()
        {
            m_ConnectedInt = 1;
            //we start with two count
            //1. receive thread,
            //2. disconnect thread 
            DisconnectEvent.Reset(2);
        }

        internal bool TrySetDisconnected()
        {
            return Interlocked.CompareExchange(ref m_ConnectedInt, 0, 1) == 1;
        }

        internal bool TryReleaseMessage(out Message message)
        {
            if (ReceiveArg.Message == null)
            {
                //true means we have received all bytes for length
                if (HandleReceiveLength())
                {
                    //now prepare a message to receive actual data
                    ReceiveArg.Message = Message.Pop();
                    PrepareReceiveMessage();
                }
            }
            else
            {
                //true means we have received all bytes for message
                if (HandleReceiveMessage())
                {
                    message = ReceiveArg.Message;
                    ReceiveArg.Message = null;
                    PrepareReceiveLength();
                    return true;
                }
            }

            message = null;
            return false;
        }

        internal void PrepareReceiveLength()
        {
            ReceiveArg.BufferList.Clear();
            ReceiveArg.BufferList.Add(new ArraySegment<byte>(m_SizeReadBuffer));
            ReceiveArg.BufferList = ReceiveArg.BufferList;
            m_BytesToReceive = m_SizeReadBuffer.Length;
        }

        internal bool HandleReceiveLength()
        {
            m_BytesToReceive -= ReceiveArg.BytesTransferred;
            if (m_BytesToReceive == 0)
            {
                //now we have size howmuch we can receive, set it
                m_BytesToReceive = MessageReader.ReadUInt16(m_SizeReadBuffer);
                return true;
            }
            //this is rare case, only one byte is received due to packet drop, we have to wait for another byte
            ReceiveArg.BufferList[0] = new ArraySegment<byte>(m_SizeReadBuffer, m_SizeReadBuffer.Length - m_BytesToReceive, m_BytesToReceive);
            ReceiveArg.BufferList = ReceiveArg.BufferList;
            return false;
        }

        internal void PrepareReceiveMessage()
        {
            ReceiveArg.Message.BindToArgsReceive(ReceiveArg, m_BytesToReceive);
            ReceiveArg.Message.Size = m_BytesToReceive;
            m_InitialBufferCount = ReceiveArg.BufferList.Count;
        }

        private bool HandleReceiveMessage()
        {
            m_BytesToReceive -= ReceiveArg.BytesTransferred;
            //received properly
            if (m_BytesToReceive == 0) return true;
            var bytesReceived = ReceiveArg.Message.Size - m_BytesToReceive;
            Message.AdvanceRecevedOffset(ReceiveArg, m_InitialBufferCount, bytesReceived);
            return false;
        }

        internal void ClearReceiving()
        {
            ReceiveArg.ClearMessage();
            PrepareReceiveLength();
        }

        internal void BuildSocket(bool noDelay, KeepAliveOption keepAlive, int sendSize, int receiveSize)
        {
            //create new socket
            Socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

            //default settings
            Socket.NoDelay = noDelay;
            Socket.SendBufferSize = sendSize;
            Socket.ReceiveBufferSize = receiveSize;
            Socket.DualMode = true;

            //linger for reuse socket
            Socket.LingerState = new LingerOption(true, 0);

            //keep alive setting
            BitConverter.GetBytes((uint)(keepAlive.Enabled ? 1 : 0)).CopyTo(m_KeepAliveOptionCache, 0);
            BitConverter.GetBytes(keepAlive.Time).CopyTo(m_KeepAliveOptionCache, 4);
            BitConverter.GetBytes(keepAlive.Interval).CopyTo(m_KeepAliveOptionCache, 8);
            Socket.IOControl(IOControlCode.KeepAliveValues, m_KeepAliveOptionCache, null);
        }
    }
}
    
