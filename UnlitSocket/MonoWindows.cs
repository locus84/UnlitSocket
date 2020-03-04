using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace UnlitSocket
{
    internal class WindowSocket : Socket
    {
        internal SocketArgs SendArg;
        internal SocketArgs ReceiveArg;

        static ConcurrentQueue<WindowSocket> s_Added = new ConcurrentQueue<WindowSocket>();

        public static int s_Running = 0;

        public static void Run()
        {
            Interlocked.Increment(ref s_Running);
            var thread = new Thread(DoRun);
            thread.Start();
        }

        public static void Stop()
        {
            Interlocked.Decrement(ref s_Running);
        }


        static void DoRun()
        {
            List<WindowSocket> receivers = new List<WindowSocket>();
            List<WindowSocket> receiversCache = new List<WindowSocket>();

            while (s_Running > 0)
            {
                while(s_Added.TryDequeue(out var socket))
                {
                    receivers.Add(socket);
                }

                receiversCache.Clear();
                receiversCache.AddRange(receivers);
                
                if(receiversCache.Count > 0)
                {
                    Select(receiversCache, null, null, 10);
                }
                else
                {
                    Thread.Sleep(10);
                }

                foreach (var sock in receivers)
                {
                    receivers.Remove(sock);
                    sock.ReceiveArg.LastTransferred = sock.Available;
                    sock.ReceiveArg.SocketError = SocketError.Success;
                    sock.ReceiveArg.InvokeComplete(sock);
                }
            }
        }


        public WindowSocket(AddressFamily family, SocketType type, ProtocolType protocol) : base(family, type, protocol) { }

        internal bool ReceiveAsyncMonoWindows(SocketArgs e)
        {
            ReceiveArg = e;
            SocketError errorCode;
            var transforrred = Receive(e.BufferList, SocketFlags.None, out errorCode);
            if(errorCode == SocketError.Success)
            {
                e.SocketError = errorCode;
                e.LastTransferred = transforrred;
                return false;
            }
            else if(errorCode == SocketError.IOPending)
            {
                s_Added.Enqueue(this);
                return true;
            }
            else
            {
                e.SocketError = errorCode;
                e.LastTransferred = transforrred;
                return false;
            }
        }
    }

    public class MonoWindows
    {

    }


}