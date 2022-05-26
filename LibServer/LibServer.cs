using System;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using LibData;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace LibServerSolution
{
    public struct Setting
    {
        public int ServerPortNumber { get; set; }
        public string ServerIPAddress { get; set; }
        public int BookHelperPortNumber { get; set; }
        public string BookHelperIPAddress { get; set; }
        public int ServerListeningQueue { get; set; }
    }


    abstract class AbsSequentialServer
    {
        protected Setting settings;

        /// <summary>
        /// Report method can be used to print message to console in standaard formaat. 
        /// It is not mandatory to use it, but highly recommended.
        /// </summary>
        /// <param name="type">For example: [Exception], [Error], [Info] etc</param>
        /// <param name="msg"> In case of [Exception] the message of the exection can be passed. Same is valud for other types</param>

        protected void report(string type, string msg)
        {
            // Console.Clear();
            Console.Out.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>");
            if (!String.IsNullOrEmpty(msg))
            {
                msg = msg.Replace(@"\u0022", " ");
            }

            Console.Out.WriteLine("[Server] {0} : {1}", type, msg);
        }

        /// <summary>
        /// This methid loads required settings.
        /// </summary>
        protected void GetConfigurationValue()
        {
            settings = new Setting();
            try
            {
                string path = AppDomain.CurrentDomain.BaseDirectory;
                IConfiguration Config = new ConfigurationBuilder()
                    .SetBasePath(Path.GetFullPath(Path.Combine(path, @"../../../../")))
                    .AddJsonFile("appsettings.json")
                    .Build();

                settings.ServerIPAddress = Config.GetSection("ServerIPAddress").Value;
                settings.ServerPortNumber = Int32.Parse(Config.GetSection("ServerPortNumber").Value);
                settings.BookHelperIPAddress = Config.GetSection("BookHelperIPAddress").Value;
                settings.BookHelperPortNumber = Int32.Parse(Config.GetSection("BookHelperPortNumber").Value);
                settings.ServerListeningQueue = Int32.Parse(Config.GetSection("ServerListeningQueue").Value);
                // Console.WriteLine( settings.ServerIPAddress, settings.ServerPortNumber );
            }
            catch (Exception e) { report("[Exception]", e.Message); }
        }


        protected abstract void createSocketAndConnectHelpers();

        public abstract void handelListening();

        protected abstract Message processMessage(Message message);

        protected abstract Message requestDataFromHelpers(string msg);


    }

    class SequentialServer : AbsSequentialServer
    {
        // check all the required parameters for the server. How are they initialized? 
        byte[] buffer = new byte[1000];

        IPAddress serverIpAddress;
        IPEndPoint localEndPoint;
        Socket serverSocket;

        IPAddress bookHelperIpAddress;
        IPEndPoint bookHelperEndPoint;
        Socket bookHelperSocket;

        public SequentialServer() : base()
        {
            GetConfigurationValue();
        }

        /// <summary>
        /// Connect socket settings and connec
        /// </summary>
        protected override void createSocketAndConnectHelpers()
        {
            // todo: To meet the assignment requirement, finish the implementation of this method.
            // Extra Note: If failed to connect to helper. Server should retry 3 times.
            // After the 3d attempt the server starts anyway and listen to incoming messages to clients

            try
            {
                // Server Connection
                serverIpAddress = IPAddress.Parse(settings.ServerIPAddress);
                localEndPoint = new IPEndPoint(serverIpAddress, settings.ServerPortNumber);
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                serverSocket.Bind(localEndPoint);
                serverSocket.Listen(settings.ServerListeningQueue);
                Console.WriteLine("[SERVER SOCKET] Server Socket created\n");

                ConnectBookHelper();
            }
            catch (Exception)
            {
                WriteError("[SERVER ERROR] An error occured while creating the Server and BookHelper sockets");
            }
        }

        private void ConnectBookHelper()
        {
            int attempts = 3;
            int timeout = 3000;

            bookHelperIpAddress = IPAddress.Parse(settings.BookHelperIPAddress);
            bookHelperEndPoint = new IPEndPoint(bookHelperIpAddress, settings.BookHelperPortNumber);
            bookHelperSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    Console.WriteLine($"[CONNECT TO BOOKHELPER ATTEMPT {attempt}/{attempts}] Attempting to connect to BookHelper...");
                    bookHelperSocket.Connect(bookHelperEndPoint);

                    WriteSuccess($"\n[BOOK HELPER CONNECTED] Successfully connected to BookHelper, after {attempt} attempt(s)!\n");
                    return;
                }
                catch (SocketException)
                {
                    if (attempt == attempts)
                        WriteError($"[BOOK HELPER CONNECTION FAILED] Could not establish connection to BookHelper. Ran out of attempts ({attempts}).\n");
                    else
                        Console.WriteLine($"[BOOK HELPER TIMEOUT] Could not establish connection to BookHelper before timeout ({timeout}ms).");
                }
            }

        }

        /// <summary>
        /// This method starts the socketserver after initializion and listents to incoming connections. 
        /// It tries to connect to the book helpers. If it failes to connect to the helper. Server should retry 3 times. 
        /// After the 3d attempt the server starts any way. It listen to clients and waits for incoming messages from clients
        /// </summary>
        public override void handelListening()
        {
            createSocketAndConnectHelpers();

            try
            { 
                while (true)
                {
                    Console.WriteLine("[START] Accepting client connections...");
                    Socket newSocket = serverSocket.Accept();
                    WriteSuccess("[START] Connection Accepted");

                    HandleClientSession(newSocket);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

        }

        private void HandleClientSession(Socket newSocket)
        {
            while (true)
            {
                Message message = GetMessage(newSocket); // Convert data into Message object
                Message replyMessage = processMessage(message);

                Console.WriteLine($"[SEND REPLY: {replyMessage.Type}]");
                byte[] messageByte = CreateMessageJSON(replyMessage.Type, replyMessage.Content);
                newSocket.Send(messageByte);

                if (message.Type == MessageType.BookInquiry || message.Type == MessageType.Error)
                {
                    Console.WriteLine("[SERVER CLOSE]\n");
                    newSocket.Close();
                    break;
                }
            }
        }

        /// <summary>
        /// Process the message of the client. Depending on the logic and type and content values in a message it may call 
        /// additional methods such as requestDataFromHelpers().
        /// </summary>
        /// <param name="message"></param>
        protected override Message processMessage(Message message)
        {
            if (message.Type == MessageType.Hello)
            {
                Message pmReply = new Message
                {
                    Type = MessageType.Welcome,
                    Content = null
                };

                return pmReply;
            }
            else if (message.Type == MessageType.BookInquiry)
            {
                Console.WriteLine("[BOOK INQUIRY] Forwarding To Helper");
                return requestDataFromHelpers(message.Content);
            }
            else
            {
                return message;
            }
        }

        /// <summary>
        /// When data is processed by the server, it may decide to send a message to a book helper to request more data. 
        /// </summary>
        /// <param name="content">Content may contain a different values depending on the message type. For example "a book title"</param>
        /// <returns>Message</returns>
        protected override Message requestDataFromHelpers(string content)
        {
            Message HelperReply = new Message();

            try
            {
                bookHelperSocket.Send(
                    CreateMessageJSON(MessageType.BookInquiry, content));

                Message message = GetMessage(bookHelperSocket);

                HelperReply.Type = message.Type;
                HelperReply.Content = message.Content;
            }
            catch (Exception)
            {
                WriteError("There was an error while communicating with the BookHelper, probably because BookHelper is down. Sending an error message to client...");
                HelperReply.Type = MessageType.Error;
                HelperReply.Content = "Server has no access to resources; could not connect to BookHelper";
            }

            return HelperReply;
        }

        public void delay()
        {
            int m = 10;
            for (int i = 0; i <= m; i++)
            {
                Console.Out.Write("{0} .. ", i);
                Thread.Sleep(200);
            }
            Console.WriteLine("\n");
            //report("round:","next to start");
        }

        // HELPER FUNCTIONS
        private Message GetMessage(Socket socket)
        {
            int response = socket.Receive(buffer);
            string data = Encoding.ASCII.GetString(buffer, 0, response);

            try
            {
                Message message = JsonSerializer.Deserialize<Message>(data);
                Console.WriteLine($"[INCOMING MESSAGE: {message.Type}] {message.Content}");
                return message;
            }
            catch (JsonException)
            {
                WriteError("[ERROR: INCOMING MESSAGE IS UNREADABLE] Could not deserialize byte stream to Message object, possibly because the message was deformed.");

                return new Message()
                {
                    Type = MessageType.Error,
                    Content = "[SERVER ERROR RESPONSE: MESSAGE UNREADABLE] The server could not deserialize byte stream to Message object, possibly because the message was deformed."
                };
            }
        }

        private static byte[] CreateMessageJSON(MessageType messageType, string content)
        {
            Message request = new()
            {
                Type = messageType,
                Content = content
            };

            return JsonSerializer.SerializeToUtf8Bytes(request);
        }

        private static void WriteError(string text)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void WriteSuccess(string text)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}

