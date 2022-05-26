using System.Linq;
using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Threading;
// using LibData;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace LibClient
{
    public struct Setting
    {
        public int ServerPortNumber { get; set; }
        public string ServerIPAddress { get; set; }

    }

    public class Output
    {
        public string Client_id { get; set; } // the id of the client that requests the book
        public string BookName { get; set; } // the name of the book to be reqyested
        public string Status { get; set; } // final status received from the server
        public string Error { get; set; } // True if errors received from the server
        public string BorrowerName { get; set; } // the name of the borrower in case the status is borrowed, otherwise null
        public string ReturnDate { get; set; } // the email of the borrower in case the status is borrowed, otherwise null
    }

    abstract class AbsSequentialClient
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

            Console.Out.WriteLine("[Client] {0} : {1}", type, msg);
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
                // settings.ServerListeningQueue = Int32.Parse(Config.GetSection("ServerListeningQueue").Value);
            }
            catch (Exception e) { report("[Exception]", e.Message); }
        }

        protected abstract void createSocketAndConnect();
        public abstract Output handleConntectionAndMessagesToServer();
        protected abstract Message processMessage(Message message);

    }

    class SequentialClient : AbsSequentialClient
    {
        public Output result;
        public Socket clientSocket;
        public IPEndPoint serverEndPoint;
        public IPAddress ipAddress;

        public string client_id;
        private string bookName;

        private bool shutdown = false;

        byte[] buffer = new byte[1000];

        //This field is optional to use. 
        private int delayTime;
        /// <summary>
        /// Initializes the client based on the given parameters and seeting file.
        /// </summary>
        /// <param name="id">id of the clients provided by the simulator</param>
        /// <param name="bookName">name of the book to be requested from the server, provided by the simulator</param>
        public SequentialClient(int id, string bookName)
        {
            GetConfigurationValue();

            // this.delayTime = 100;
            this.bookName = bookName;
            this.client_id = "Client " + id.ToString();
            this.result = new Output();

            result.Client_id = this.client_id;
            result.BookName = bookName;
            result.Status = "Not Found"; //default
        }


        /// <summary>
        /// Optional method. Can be used for testing to delay the output time.
        /// </summary>
        public void delay()
        {
            int m = 10;
            for (int i = 0; i <= m; i++)
            {
                Console.Out.Write("{0} .. ", i);
                Thread.Sleep(delayTime);
            }
            Console.WriteLine("\n");
        }

        /// <summary>
        /// Connect socket settings and connect to the helpers.
        /// </summary>
        protected override void createSocketAndConnect()
        {
            try
            {
                ipAddress = IPAddress.Parse(settings.ServerIPAddress);
                serverEndPoint = new IPEndPoint(ipAddress, settings.ServerPortNumber);
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Console.WriteLine("[CLIENT SOCKET CREATED]");

                clientSocket.Connect(serverEndPoint);
                Console.WriteLine("[CONNECTED TO SERVER]\n");
            }
            catch (Exception)
            {
                WriteError("[CRITICAL SERVER/SOCKET ERROR] Could not establisth a connection to the server. Probably because it is offline.");
            }

        }

        /// <summary>
        /// This method starts the socketserver after initializion and handles all the communications with the server. 
        /// Note: The signature of this method must not change.
        /// </summary>
        /// <returns>The final result of the request that will be written to output file</returns>
        public override Output handleConntectionAndMessagesToServer()
        {
            this.report("starting:", this.client_id + " ; " + this.bookName);
            createSocketAndConnect();

            //todo: To meet the assignment requirement, finish the implementation of this method.
            try
            {
                // Initial Message "Hello"
                clientSocket.Send(CreateMessageJSON(MessageType.Hello, client_id));
                //clientSocket.Send(Encoding.ASCII.GetBytes("hello")); // a test 'deformed' message
                Console.WriteLine("[SENDING MESSAGE] Hello");

                while (shutdown == false)
                {
                    Message message = GetMessage();
                    processMessage(message);
                }

                Console.WriteLine("[SOCKET CLOSE] Client Socket Closed");
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                WriteError("[CONNECTION ERROR] Message could not be sent to the server. Probably because it is offline.");
                result.Error = "true";
            }

            return result;
        }

        /// <summary>
        /// Process the messages of the server. Depending on the logic, type and content of a message the client may return different message values.
        /// </summary>
        /// <param name="message">Received message to be processed</param>
        /// <returns>The message that needs to be sent back as the reply.</returns>
        protected override Message processMessage(Message message)
        {
            if (message.Type == MessageType.Error)
            {
                WriteError(message.Content);
                result.Error = "true";
                shutdown = true;
                return null;
            }

            Console.WriteLine($"[INCOMING MESSAGE: {message.Type}] {message.Content}");

            if (message.Type == MessageType.Welcome)
            {
                Console.WriteLine("[SENDING MESSAGE: BookInquiry]");
                clientSocket.Send(CreateMessageJSON(MessageType.BookInquiry, bookName));
                return null;
            }

            if (message.Type == MessageType.BookInquiryReply)
            {
                Console.WriteLine("[CREATING OUTPUT]");
                CreateOutput(JsonSerializer.Deserialize<BookData>(message.Content));
                shutdown = true;
            }
            else if (message.Type == MessageType.NotFound)
            {
                Console.WriteLine("[CREATING OUTPUT]");
                shutdown = true;
            }

            return null;
        }

        private void CreateOutput(BookData bookData)
        {
            result.BorrowerName = bookData.BorrowedBy;
            result.ReturnDate = bookData.ReturnDate;
            result.Status = bookData.Status;
        }

        private Message GetMessage()
        {
            int response = clientSocket.Receive(buffer);
            string data = Encoding.ASCII.GetString(buffer, 0, response);

            Message message;

            try
            {
                message = JsonSerializer.Deserialize<Message>(data);
                return message;
            }
            catch (Exception)
            {
                WriteError("[ERROR: INCOMING MESSAGE IS UNREADABLE] Could not deserialize byte stream to Message object, possibly because the message was deformed.");

                return new Message()
                {
                    Type = MessageType.Error,
                    Content = "[CLIENT ERROR RESPONSE: MESSAGE UNREADABLE] Could not deserialize byte stream to Message object, possibly because the message is deformed"
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
    }
}

