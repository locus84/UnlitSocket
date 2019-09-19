using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class UserToken
    {
        public bool IsConnected = false;
        int m_ReadTotal = 0;
        int m_SizeTotal = 0;
        int m_InitialBufferCount = 0;
        public int ConnectionID { get; private set; }
        public Socket Socket { get; set; }
        public int LastTransferCount;

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

        public UserToken(int id)
        {
            ConnectionID = id;
            ReceiveArg = new SocketAsyncEventArgs();
            ReceiveArg.BufferList = new List<ArraySegment<byte>>();
            ReceiveArg.UserToken = this;
        }
    }
}
    
