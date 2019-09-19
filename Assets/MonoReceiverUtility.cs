using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace UnlitSocket
{
    public static class MonoReceiverUtility
    {
        static ConcurrentQueue<SocketAsyncEventArgs> s_ReceivePostBag = new ConcurrentQueue<SocketAsyncEventArgs>();
        const int MAX_THREAD_COUNT = 1;
        static int s_CurrentThreadCount = 0;

        public static bool ReceiveAsyncWithMono(this Socket socket, SocketAsyncEventArgs args)
        {
            if(socket.Available > 0)
            {
                try
                {
                    ((UserToken)args.UserToken).LastTransferCount = socket.Receive(args.BufferList, args.SocketFlags);
                    args.SocketError = SocketError.Success;
                }
                catch
                {
                    ((UserToken)args.UserToken).LastTransferCount = 0;
                    args.SocketError = SocketError.OperationAborted;
                }
                return false;
            }
            else
            {
                args.AcceptSocket = socket;
                s_ReceivePostBag.Enqueue(args);

                if(s_CurrentThreadCount < MAX_THREAD_COUNT)
                {
                    var count = Interlocked.Increment(ref s_CurrentThreadCount);
                    if(count <= MAX_THREAD_COUNT)
                    {
                        var newThread = new Thread(ReceiveThreadLoop);
                        newThread.Start();
                    }
                }
                return true;
            }
        }

        static void ReceiveThreadLoop(object state)
        {
            List<SocketAsyncEventArgs> observingPosts = new List<SocketAsyncEventArgs>();
            while (true)
            {
                while (s_ReceivePostBag.TryDequeue(out var result))
                    observingPosts.Add(result);

                for (int i = 0; i < observingPosts.Count; i++)
                {
                    var currentPost = observingPosts[i];

                    try
                    {
                        if (currentPost.AcceptSocket.Poll(0, SelectMode.SelectRead))
                        {
                            //just to move socket to worker thread
                            ThreadPool.QueueUserWorkItem(OnInvokeWorkerThread, currentPost);
                            observingPosts[i] = observingPosts[observingPosts.Count - 1];
                            observingPosts.RemoveAt(observingPosts.Count - 1);
                            i--;
                        }
                    }
                    catch (SocketException)
                    {
                        currentPost.SocketError = SocketError.SocketError;
                        currentPost.AcceptSocket = null;
                        ThreadPool.QueueUserWorkItem(OnInvokeWorkerThreadFailed, currentPost);
                        observingPosts[i] = observingPosts[observingPosts.Count - 1];
                        observingPosts.RemoveAt(observingPosts.Count - 1);
                        i--;
                    }
                    catch (System.ObjectDisposedException)
                    {
                        currentPost.SocketError = SocketError.OperationAborted;
                        currentPost.AcceptSocket = null;
                        ThreadPool.QueueUserWorkItem(OnInvokeWorkerThreadFailed, currentPost);
                        observingPosts[i] = observingPosts[observingPosts.Count - 1];
                        observingPosts.RemoveAt(observingPosts.Count - 1);
                        i--;
                    }
                }
                Thread.Sleep(3);
            }
        }

        static void OnInvokeWorkerThreadFailed(object state)
        {
            OnCompleteInvoker.InvokeOnComplete((SocketAsyncEventArgs)state);
        }

        static void OnInvokeWorkerThread(object state)
        {
            var args = (SocketAsyncEventArgs)state;
            var socket = args.AcceptSocket;
            args.AcceptSocket = null;

            try
            {
                ((UserToken)args.UserToken).LastTransferCount = socket.Receive(args.BufferList, args.SocketFlags);
                args.SocketError = SocketError.Success;
                OnCompleteInvoker.InvokeOnComplete(args);
            }
            catch
            {
                ((UserToken)args.UserToken).LastTransferCount = 0;
                args.SocketError = SocketError.OperationAborted;
                OnCompleteInvoker.InvokeOnComplete(args);
            }
        }

        class OnCompleteInvoker : SocketAsyncEventArgs
        {

            protected override void OnCompleted(SocketAsyncEventArgs e)
            {
                UnityEngine.Debug.Log("Complete!");
                base.OnCompleted(e);
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct ArgsConverter
            {
                [FieldOffset(0)]
                public SocketAsyncEventArgs ArgsOriginal;

                [FieldOffset(0)]
                public OnCompleteInvoker Converted;
            }

            public static void InvokeOnComplete(SocketAsyncEventArgs args)
            {
                ArgsConverter converter = new ArgsConverter();
                converter.ArgsOriginal = args;
                converter.Converted.OnCompleted(args);
            }
        }
    }
}

