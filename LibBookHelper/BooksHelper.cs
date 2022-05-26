using System;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using LibData;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace BookHelperSolution
{
    public struct Setting
    {
        public int BookHelperPortNumber { get; set; }
        public string BookHelperIPAddress { get; set; }
        public int ServerListeningQueue { get; set; }
    }

    abstract class AbsSequentialServerHelper
    {
        protected Setting settings;
        protected string booksDataFile;

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

            Console.Out.WriteLine("[Server Helper] {0} : {1}", type, msg);
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

                settings.BookHelperIPAddress = Config.GetSection("BookHelperIPAddress").Value;
                settings.BookHelperPortNumber = Int32.Parse(Config.GetSection("BookHelperPortNumber").Value);
                settings.ServerListeningQueue = Int32.Parse(Config.GetSection("ServerListeningQueue").Value);
            }
            catch (Exception e) { report("[Exception]", e.Message); }
        }

        protected abstract void loadDataFromJson();
        protected abstract void createSocket();
        public abstract void handelListening();
        protected abstract Message processMessage(Message message);

    }



    class SequentialServerHelper : AbsSequentialServerHelper
    {
        // check all the required parameters for the server. How are they initialized? 
        public Socket listener;
        public IPEndPoint listeningPoint;
        public IPAddress ipAddress;
        public List<BookData> booksList;

        byte[] buffer = new byte[1000];

        public SequentialServerHelper() : base()
        {
            booksDataFile = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"../../../") + "Books.json");
            //booksDataFile = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"../../") + "Books.json");
            GetConfigurationValue();
            loadDataFromJson();
        }

        /// <summary>
        /// This method loads data items provided in booksDataFile into booksList.
        /// </summary>
        protected override void loadDataFromJson()
        {
            //todo: To meet the assignment requirement, implement this method 
            try
            {
                string inputContent = File.ReadAllText(booksDataFile);
                booksList = JsonSerializer.Deserialize<List<BookData>>(inputContent);
                Console.WriteLine("[READ DATABASE] Books.json has been loaded in.");
            }
            catch (Exception e)
            {
                WriteError("[FATAL BOOKHELPER ERROR] Stopping BookHelper server.");
                WriteError(e.Message);
                Environment.Exit(1);    
            }
        }

        /// <summary>
        /// This method establishes required socket: listener.
        /// </summary>
        protected override void createSocket()
        {
            //todo: To meet the assignment requirement, implement this method
            try
            {
                ipAddress = IPAddress.Parse(settings.BookHelperIPAddress);
                listeningPoint = new IPEndPoint(ipAddress, settings.BookHelperPortNumber);
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                listener.Bind(listeningPoint);
                listener.Listen(settings.ServerListeningQueue);

                Console.WriteLine("[BOOK HELPER SOCKET CREATED] Accepting connection from Server...");
            }
            catch (Exception)
            {
                WriteError("[CRITICAL SERVER/SOCKET ERROR] Error In method 'createSocket()'");
            }

        }

        /// <summary>
        /// This method handles all the communications with the LibServer.
        /// </summary>
        public override void handelListening()
        {
            createSocket();

            Socket newSocket = listener.Accept();

            WriteSuccess("\n[START] Connection to server has been established\n");

            try
            {
                while (true)
                {
                    Message message = GetMessage(newSocket);

                    if (message.Type == MessageType.BookInquiry)
                    {
                        Console.WriteLine($"[SEARCHING FOR BOOK...]");
                        BookData book = booksList.Find(book => book.Title == message.Content);

                        if (book != null)
                        {
                            Console.WriteLine("[SEND MESSAGE BookInquiryReply] Book was found. Sending BookInquiryReply message to server.\n");
                            newSocket.Send(CreateMessageJSON(MessageType.BookInquiryReply, book));
                        }
                        else
                        {
                            Console.WriteLine("[SEND MESSAGE NotFound] Book could not be found. Sending NotFound message to server.\n");
                            newSocket.Send(CreateMessageJSON(MessageType.NotFound, "BookHelper could not find specified book"));
                        }
                    }
                }
            } catch (SocketException)
            {
                WriteError("[LISTENING HANDLER ERROR] Something went from while trying to read incoming messages, probably because the remote host went offline.");
            }

            Console.WriteLine("[END] Closing BookHelper Socket");
            listener.Close();
        }

        /// <summary>
        /// Given the message received from the Server, this method processes the message and returns a reply.
        /// </summary>
        /// <param name="message">The message received from the LibServer.</param>
        /// <returns>The message that needs to be sent back as the reply.</returns>
        protected override Message processMessage(Message message)
        {
            Message reply = new Message();
            //todo: To meet the assignment requirement, finish the implementation of this method .
            // try
            // {

            // }
            // catch (Exception e)
            // {

            // }
            return reply;
        }

        private static void WriteError( string text)
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
                    Content = "[BOOKHELPER ERROR RESPONSE: MESSAGE UNREADABLE] BookHelper could not deserialize byte stream to Message object, possibly because the message was deformed."
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

        private static byte[] CreateMessageJSON(MessageType messageType, BookData bookData)
        {
            Message request = new()
            {
                Type = messageType,
                Content = JsonSerializer.Serialize(bookData)
            };

            return JsonSerializer.SerializeToUtf8Bytes(request);
        }
    }
}
