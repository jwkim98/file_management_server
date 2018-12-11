using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.IO;


namespace Server
{
    public class Network // This class manages network communication between Server and Client
    {
        private readonly int _backlog; //Specifies number of incoming connections that can be queued;
        public int MaximumClient { get; private set; }

        private readonly object _lockObject = new Object(); //for using mutex

        private CommunicatorPool _communicatorPool;
        private UserPool _userPool;
        private Listener _listen;

        private delegate void ClientAcceptEvent();

        public Network(int maximumClients)
        {
            _backlog = maximumClients;
            InitializePool(maximumClients);
            MaximumClient = maximumClients;
        }

        public void SetDlcPath(string directoryPath)
        {
            FileBaseDirectory.DlcDir = directoryPath;
        }

        public void SetUserPath(string directoryPath)
        {
            FileBaseDirectory.UserDir = directoryPath;
        }

        public void CreateDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath + @"/dlc") == false)
                {
                    Console.WriteLine("Creating New dlc Directory..");
                    Directory.CreateDirectory(directoryPath + @"/dlc");
                    Directory.CreateDirectory(directoryPath + @"/dlc/bg");
                    Directory.CreateDirectory(directoryPath + @"/dlc/ecg");
                    Directory.CreateDirectory(directoryPath + @"/dlc/scg");
                    Directory.CreateDirectory(directoryPath + @"/dlc/scripts");
                    Console.WriteLine("The directory was created successfully at {0}.",
                        Directory.GetCreationTime(directoryPath + @"/dlc"));
                }
                else
                {
                    Console.WriteLine("dlcDirectory already exists");
                }

                if (Directory.Exists(directoryPath + @"/user") == false)
                {
                    Console.WriteLine("Creating New user Directory..");
                    Directory.CreateDirectory(directoryPath + @"/user");
                    Directory.CreateDirectory(directoryPath + @"/user/0");
                    Console.WriteLine("The directory was created successfully at {0}.",
                        Directory.GetCreationTime(directoryPath + @"/user"));
                }
                else
                {
                    Console.WriteLine("userDirectory already exists");
                }
                FileBaseDirectory.DlcDir = directoryPath + @"/dlc";
                FileBaseDirectory.UserDir = directoryPath + @"/user";
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        } //directoryPath is the parent path for dlc and user folder

        public int BufferSize { get; private set; } = PacketConsts.ArgsBufferSize;

        public void Start_Listen(string ipAddress, int portNum)
        {
            if (FileBaseDirectory.DlcDir == null || FileBaseDirectory.UserDir == null)
            {
                Console.WriteLine("Please specify file path");
            }

            if (Directory.Exists(FileBaseDirectory.DlcDir) == false)
            {
                Console.WriteLine("No directory on DlcPath");
                return;
            }

            if (Directory.Exists(FileBaseDirectory.UserDir) == false)
            {
                Console.WriteLine("No directory on UserPath");
            }
            
            _listen = new Listener(_communicatorPool);
            if (ipAddress != null) _listen.Host = ipAddress;
            if (portNum > 1023) _listen.Port = portNum;
            _listen.CallBackonNewclient += Newclient;//add Newclient callback handler to delegate
            //this delegate is called when new client is accepted
            _listen.ActivateAcception = true;
            _listen.Start(_backlog);
            while (true)
            {
  //              Thread.Sleep(1500);
                Console.ReadLine();
                Console.WriteLine("Communicator Stack size:"+_communicatorPool.StackSize());
            }
        }

        public void InitializePool(int maximumClients)//This method should be called before 
        {

            // _userPool=new UserPool(maximumClients);
            _communicatorPool = new CommunicatorPool(maximumClients);
            _userPool = new UserPool(maximumClients);
            BufferManager sendBuffer = new BufferManager(maximumClients, BufferSize);
            BufferManager receiveBuffer = new BufferManager(maximumClients, BufferSize);
            for (int i = 0; i < maximumClients; i++)// Fill out the stack pool Initially
            {
                SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs();// Create new Socket Pool for sending and receiving
                SocketAsyncEventArgs receiveArg = new SocketAsyncEventArgs();

                sendBuffer.SetBuffer(sendArg); // Set buffer for sendArg and receiveArg objects 
                receiveBuffer.SetBuffer(receiveArg);

                _communicatorPool.Push(new Communicator(sendArg, receiveArg));
                _userPool.Push(new User(0));

                //_userPool.Push(new User(i));// put UserToken and UserID for the user initially
            }
        }

        private void Newclient(Socket clientSocket, object token)
        {
            while (_communicatorPool.StackSize() == 0)
            {
                Thread.Sleep(100);//wait until Resource is ready
            }
            if (_communicatorPool.StackSize() > 0)
            {
                Communicator communicator = _communicatorPool.Pop();
                User user = _userPool.Pop();

                user.SetUserSocket(null); //initialize members before using
                communicator.SetUser(null);
                communicator.OperationState = true;
                communicator.ReceivedBytesTransferredCount = 0;
                communicator.SentBytesTransferredCount = 0;
                user.UserId = 0;

                ServerProcessManager manager = new ServerProcessManager(user, communicator);
                manager.Start(clientSocket);
                ResetAssets(communicator, user);
                _userPool.Push(user);
               _communicatorPool.Push(communicator);//push it back after operation
            }
        }

        private void ResetAssets(Communicator communicator, User user)
        {
            user.SendQueue = new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());
            user.ReceiveQueue = new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());
            communicator.OperationState = false;
            lock (communicator.CancelSendDequeue)
            {
                communicator.CancelSendDequeue.Cancel();
                communicator.CancelSendDequeue = new CancellationTokenSource();
            }
            try
            {
                communicator.UserToken.ClientSocket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }
            while (communicator.UserToken.ClientSocket.Connected)
            {
                communicator.UserToken.ClientSocket.Disconnect(false);
            }
            communicator.UserToken.ClientSocket.Close(); //close the socket
            Thread.Sleep(3000);// wait time for ongoing processes to terminate
            GC.Collect();//Perform garbage collection
        }

        public int UserCount()
        {
            lock (_lockObject)
            {
                return MaximumClient - _communicatorPool.StackSize();
            }
        }

        public int CommunicatorStackSize()
        {
            return _communicatorPool.StackSize();
        }

        public int UserStackSize()
        {
            return _userPool.StackSize();
        }

        public void DisableAcception()
        {
            _listen.ActivateAcception = false;
        }

        public void EnableAcception()
        {
            _listen.ActivateAcception = true;
        }

    }

}



