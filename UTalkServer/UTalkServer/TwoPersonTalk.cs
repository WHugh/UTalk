using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UTalkServer
{
    public class TwoPersonTalk
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        private static ManualResetEvent client1Done = new ManualResetEvent(false);
        private static ManualResetEvent client2Done = new ManualResetEvent(false);
        private static ManualResetEvent hasTwoSocket = new ManualResetEvent(false);

        static int i = 0;//控制，只有两个连接
        static Socket[] clientSocket = new Socket[2];
        public static string ServerIP = string.Empty;

        static Thread th1 = new Thread(Client1Run);
        static Thread th2 = new Thread(Client2Run);
        public TwoPersonTalk() { }

        public static void StartTalking()
        {
            IPEndPoint sEndp = new IPEndPoint(IPAddress.Parse(ServerIP), 11000);
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(sEndp);
            listener.Listen(0);
            Console.WriteLine("Start listening...");

            th1.Start();
            th2.Start();
            //AsynAccecpt
            allDone.Set();
            while (true)
            {
                //控制连接个数小于等于2
                if (i == 2)
                {
                    allDone.Reset();
                    hasTwoSocket.Set();
                }
                allDone.WaitOne();
                allDone.Reset();
                listener.BeginAccept(new AsyncCallback(AcceptCallBack), listener);
                allDone.WaitOne();
            }
        }
        public static void AcceptCallBack(IAsyncResult ar)
        {
            Socket handle = (Socket)ar.AsyncState;
            Socket serverSocket = handle.EndAccept(ar);
            clientSocket[i] = null;
            clientSocket[i] = serverSocket;
            Console.WriteLine("成功连接：" + serverSocket.RemoteEndPoint.ToString());
            ++i;
            allDone.Set();
        }
        public static void Client1Run()
        {
            while (true)
            {
                try
                {
                    client1Done.Reset();
                    StateObject state = new StateObject(1);
                    state.workSocket = clientSocket[0];
                    clientSocket[0].BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                new AsyncCallback(ReceiveCallBack), state);
                    client1Done.WaitOne();
                    hasTwoSocket.WaitOne();
                }
                catch (NullReferenceException)
                {
                    hasTwoSocket.Reset();
                    hasTwoSocket.WaitOne();
                    continue;
                }                
            }
        }
        public static void Client2Run()
        {
            while (true)
            {
                try
                {
                    client2Done.Reset();
                    StateObject state = new StateObject(2);
                    state.workSocket = clientSocket[1];
                    clientSocket[1].BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                new AsyncCallback(ReceiveCallBack), state);
                    client2Done.WaitOne();
                    hasTwoSocket.WaitOne();
                }
                catch (NullReferenceException)
                {
                    hasTwoSocket.Reset();
                    hasTwoSocket.WaitOne();
                    continue;
                }
            }
        }
        
        public static void ReceiveCallBack(IAsyncResult ar)
        {
            try
            {
                string content = string.Empty;
                StateObject state = (StateObject)ar.AsyncState;
                Socket handle = state.workSocket;
                int bytes = handle.EndReceive(ar);
                state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytes));
                if (state.sb.ToString().IndexOf("<EOF>") > -1)//读取完毕
                {
                    content = state.sb.ToString();
                    Console.WriteLine("From " + state.workSocket.RemoteEndPoint.ToString() + ": " + content.Split('*')[0]);
                    string[] data = content.Split(':');
                    if (state.client == 1 && i == 2)
                    {
                        client1Done.Set();
                        Send(clientSocket[1], "Ta: " + content);
                    }
                    if (state.client == 2 && i == 2)
                    {
                        client2Done.Set();
                        Send(clientSocket[0], "Ta: " + content);
                    }
                }
                else
                {
                    handle.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallBack), state);
                }
            }
            catch (SocketException)
            {
                --i;
                StateObject state = (StateObject)ar.AsyncState;
                if (state.client == 1)
                {
                    clientSocket[0] = clientSocket[1];
                }
                

                hasTwoSocket.Reset();
                allDone.Set();
                
                if (state.client == 1) client1Done.Set();
                if (state.client == 2) client2Done.Set();
                
                Console.WriteLine("客户端连接 <{0}> 被关闭了~", state.workSocket.RemoteEndPoint.ToString());
                if (state.client == 1 && i > 0)
                {
                    Send(clientSocket[1], "Server: 客户端连接 <" + state.workSocket.RemoteEndPoint.ToString() + 
                        "> 被关闭了*<EOF>");
                }
                if (state.client == 2 && i > 0)
                {
                    Send(clientSocket[0], "Server: 客户端连接 <" + state.workSocket.RemoteEndPoint.ToString() + 
                        "> 被关闭了*<EOF>");
                }
            }
        }
        public static void Send(Socket handle, string data)
        {
            byte[] byteData = Encoding.UTF8.GetBytes(data);
            handle.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallBack), handle);
        }
        public static void SendCallBack(IAsyncResult ar)
        {
            Socket handle = (Socket)ar.AsyncState;
            int bytesSent = handle.EndSend(ar);
        }
        
    }
}
