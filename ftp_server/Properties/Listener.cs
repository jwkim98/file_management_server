using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Server
{
    public class Listener
    {
        //private String host = "0.0.0.0";//default host and port number
        private SocketAsyncEventArgs _acceptArgs;

        private Socket _listenSocket;

        private AutoResetEvent _flowControlEvent;

        private Thread _listenThread;


        internal delegate void NewclientHandler(Socket clientSocket, object token);
        internal NewclientHandler CallBackonNewclient;
        private Thread _pendingThread;
        public String Host { get; set; } = "0.0.0.0";//properties for setting host and port
        private readonly CommunicatorPool _communicatorPool;

        public int Port { get; set; } = 2018;// Initial port number must be larger than 1023

        public bool ActivateAcception { get; internal set; }

        public Listener(CommunicatorPool communicatorPool)
        {
            CallBackonNewclient = null;//initialize delegate
            _communicatorPool = communicatorPool;
        }

        public void Start(int backlog)
        {
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            IPAddress address;
            if (Host == "0.0.0.0")
            {
                Console.WriteLine("IP Address not Specified.. Finding new IP Address. ");
                address = IPAddress.Any;//if IP address is not specified, get IP address that server is connected
            }
            else
            {
                try
                {
                    address = IPAddress.Parse(Host);
                }
                catch (FormatException)
                {
                    Console.WriteLine("Wrong IPAddress Format..");
                    return;
                }
                catch (ArgumentNullException)
                {
                    Console.WriteLine("IPAddress input was NULL");
                    return;
                }
            }

            try
            {
                IPEndPoint endpoint = new IPEndPoint(address, Port);//make endpoint by IPAdress, and port number(port number>1023)
                DisplayEndpointInfo(endpoint);
                _listenSocket.Bind(endpoint);
                _listenSocket.Listen(backlog);//ready to accept client

                _acceptArgs = new SocketAsyncEventArgs();// Socket for accepting clients
                _acceptArgs.Completed += On_Accept_Completed;//attaching event handler
                                                             //event attached here will be executed when client is accepted
                _flowControlEvent = new AutoResetEvent(false);
                _listenThread = new Thread(Threaded_accept);//this thread executes 'threaded_accept'
                //Console.WriteLine("Starting The Listening Thread..");
                _listenThread.Name = "ListenThread";
                _listenThread.Start();

            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured while starting Server Type:" + e.Message);
                Console.WriteLine("Terminating process due to the Exception..");
            }
        }

        private void Threaded_accept()//this function accepts client. It's executed on new thread
        {
            while (ActivateAcception)
            {
                if (_communicatorPool.StackSize() > 10)
                {
                    Console.WriteLine("Accepting");
                    _acceptArgs.AcceptSocket = null; // Set to null for receiving new client socket
                    bool pending = _listenSocket.AcceptAsync(_acceptArgs); // If completed Asynchronously
                    //On_Accept_Completed is called Automatically

                    if (pending == false) // If AcceptAsync was completed synchronously
                    {
                        _pendingThread = new Thread(StartNewThread);
                        _pendingThread.Start(); //This is for keep receiving requests while Thread is working
                    }

                    //waits until socket is accepted
                    _flowControlEvent.WaitOne(); 
                }
            }
        }

        private void StartNewThread()
        {
            On_Accept_Completed(null, _acceptArgs);//call the method explicitly
        }

        private void On_Accept_Completed(object sender, SocketAsyncEventArgs acceptedArg)
        {
            if (acceptedArg.SocketError == SocketError.Success)
            {
                Console.WriteLine("Client Accepted");
                Socket clientSocket = acceptedArg.AcceptSocket;
                _flowControlEvent.Set();// Let main thread accept new clients
                CallBackonNewclient?.Invoke(clientSocket, null);// Call Newclient method by delegate if CallBackonNewclient is not null
                return;
            }

            Console.WriteLine("Couldn't accept the client");
            Console.WriteLine("Error type: {0}", acceptedArg.SocketError);
            _flowControlEvent.Set();
            //Control shouldn't reach here
        }

        public void AbortAccept()//Never call this method unless emergency!
        {
            _listenThread.Abort();
            Console.WriteLine("System Aborted!");
            _listenThread.Join();
        }

        private static void DisplayEndpointInfo(IPEndPoint endpoint)
        {
            Console.WriteLine("<IPEndpoint Info>");
            Console.WriteLine("Endpoint.Address : " + endpoint.Address);
            Console.WriteLine("Endpoint.AddressFamily : " + endpoint.AddressFamily);
            Console.WriteLine("Endpoint.Port : " + endpoint.Port);
            Console.WriteLine("Endpoint.ToString() : " + endpoint);
        }
    }
}
