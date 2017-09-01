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
        public static ManualResetEvent allDone = new ManualResetEvent(false);//控制连接
        private static ManualResetEvent client1Done = new ManualResetEvent(false);//控制线程1，clientSocket[0]
        private static ManualResetEvent client2Done = new ManualResetEvent(false);//控制线程2，clientSocket[1]
        private static ManualResetEvent hasTwoSocket = new ManualResetEvent(false);//控制连接个数，只有连接为两个的时候才可以聊天

        static int i = 0;//总连接数，也用来控制，最多两个连接
        static Socket[] clientSocket = new Socket[2];
        public static string ServerIP = string.Empty;

        static Thread th1 = new Thread(Client1Run);
        static Thread th2 = new Thread(Client2Run);
        public TwoPersonTalk() { }

        public static void StartTalking()
        {
            IPEndPoint sEndp = new IPEndPoint(IPAddress.Parse(ServerIP), 11000);//Server的IPEndPoint
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
                    allDone.Reset();//连接个数为两个时，服务端停止接入新的客户端
                    hasTwoSocket.Set();//控制，允许开始聊天
                }
                allDone.WaitOne();//连接数小于2可以通过
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
        public static void Client1Run()//clientSocket[0]在这里跑
        {
            while (true)
            {
                try
                {
                    client1Done.Reset();
                    StateObject state = new StateObject(1);//state.client = 1，标识该state.workSocket为clientSocket[0]
                    state.workSocket = clientSocket[0];
                    clientSocket[0].BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                new AsyncCallback(ReceiveCallBack), state);
                    client1Done.WaitOne();
                    hasTwoSocket.WaitOne();//连接数小于2时不允许继续聊天，直到连接数重新变成2
                }
                catch (NullReferenceException)//程序一开始时，clientSocket[0]和[1]都==null，防止程序抛出异常
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
                    StateObject state = new StateObject(2);//state.client = 2，标识该state.workSocket为clientSocket[1]
                    state.workSocket = clientSocket[1];
                    clientSocket[1].BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                new AsyncCallback(ReceiveCallBack), state);
                    client2Done.WaitOne();
                    hasTwoSocket.WaitOne();//连接数小于2时不允许继续聊天，直到连接数重新变成2
                }
                catch (NullReferenceException)//程序一开始时，clientSocket[0]和[1]都==null，防止程序抛出异常
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
                string content = string.Empty;//接收的数据
                StateObject state = (StateObject)ar.AsyncState;
                Socket handle = state.workSocket;
                int bytes = handle.EndReceive(ar);//客户端非正常关闭（点叉关闭）时抛出异常SocketException
                //把每次读入到buffer里的数据添加到state的stringBuilder里
                state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytes));
                if (state.sb.ToString().IndexOf("<EOF>") > -1)//读取完毕，数据以<EOF>结束
                {
                    content = state.sb.ToString();
                    Console.WriteLine("From " + state.workSocket.RemoteEndPoint.ToString() + 
                        ": " + content.Split('*')[0]);
                    //string[] data = content.Split(':');
                    //只有当连接数是2的时候才可以发送给另一个客户端
                    if (state.client == 1 && i == 2)//clientSocket[0]，即th1线程在跑这个回调函数
                    {
                        client1Done.Set();//数据已经接收完了，th1可以继续跑
                        Send(clientSocket[1], "Ta: " + content);//服务端将数据发送给另一个客户端
                    }
                    if (state.client == 2 && i == 2)
                    {
                        client2Done.Set();
                        Send(clientSocket[0], "Ta: " + content);
                    }
                }
                else//未读取完毕，继续读取到该state.buff里
                {
                    handle.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallBack), state);
                }
            }
            catch (SocketException)//捕获EndReceive(IAsyncResult ar)抛出的异常，客户端非正常关闭引起的
            {
                --i;//每捕获一个异常表明有一个客户端断开了连接，那么总连接数减1
                StateObject state = (StateObject)ar.AsyncState;
                //如果只断开一个时，即第一个断开连接的是clientSocket[0]，那么就要让clientSocket[0] = clientSocket[1]
                //这样保证了新接入一个连接后，clientSocket[0]和[1]都是连接的
                //否则新接入的覆盖了之前处于连接状态的clientSocket[1]
                //但是clientSocket[0]还是断开的，可是i已经从1++至2，服务端不再接入新的客户端
                //那么就会抛出SocketException导致程序中止
                //如果两个都断开或第一个断开的是clientSocket[1]，i--就可以了
                //这时候控制hasTwoSocket就好了，新接入的连接自动覆盖之前的连接
                if (state.client == 1)
                {
                    clientSocket[0] = clientSocket[1];
                }
                

                hasTwoSocket.Reset();//让两个线程都等待，直到连接数变成2再继续运行
                allDone.Set();//服务端开始允许接入新的连接

                //在hasTwoSocket.Reset();之后client[x]Done.Set();将阻塞权交给hasTwoSocket
                //如果不设为set的话，那么程序就一直阻塞在了client[x]Run的client[x]Done.WaitOne()那里了
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
