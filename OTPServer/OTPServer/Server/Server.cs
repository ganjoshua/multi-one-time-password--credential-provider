﻿/* * * * * * * * * * * * * * * * * * * * *
**
** Copyright 2012 Dominik Pretzsch
** 
**    Licensed under the Apache License, Version 2.0 (the "License");
**    you may not use this file except in compliance with the License.
**    You may obtain a copy of the License at
** 
**        http://www.apache.org/licenses/LICENSE-2.0
** 
**    Unless required by applicable law or agreed to in writing, software
**    distributed under the License is distributed on an "AS IS" BASIS,
**    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
**    See the License for the specific language governing permissions and
**    limitations under the License.
**
** * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace OTPServer.Server
{
    public class Server
    {
        // TODO: Move to config
        public const int PORT = 16588;
        public const int CLIENT_MAX_AGE  = 1; // Minutes
        public const int MAX_CONNECTIONS = 250;

        private static Dictionary<int, HandleClient> __ClientHandles;
        private Thread _MaintainingThread = null;
        private Thread _ListeningThread = null;

        private static IPAddress __IPADDR = null;
        public static IPAddress IPADDR
        {
            get { return __IPADDR; }
        }

        public static int ConnectionCount
        {
            get { return __ClientHandles.Count; }
        }

        private static bool __Active;
        public static bool Active
        {
            get { return __Active; }
        }

        private static Server __Instance = null;
        public static Server Instance
        {
            get
            {
                if (__Instance == null)
                    __Instance = new Server();
                return __Instance;
            }
        }

        private Server()
        {
            __IPADDR = IPAddress.Any;

            __Active = false;
            if (__ClientHandles == null)
                __ClientHandles = new Dictionary<int, HandleClient>();            
        }

        ~Server()
        {
            __Active = false;
            __ClientHandles = null;

            _ListeningThread = null;
            _MaintainingThread = null;
        }

        public bool Start()
        {
            __Active = true;

            Listen(false);
            MaintainClientHandles(false);

            return true;
        }

        public bool Stop()
        {
            __Active = false;

            if (_MaintainingThread != null)
            {
                _MaintainingThread.Abort();
                _MaintainingThread.Join();
            }

            if (_ListeningThread != null)
            {
                _ListeningThread.Abort();
                _ListeningThread.Join();
            }

            return true;
        }

        private void Listen()
        {
            Listen(true);
        }

        private void Listen(bool isThread)
        {
            TcpListener listener;

            if (!isThread && _ListeningThread == null)
            {
                _ListeningThread = new Thread(Listen);
                _ListeningThread.Start();
                return;
            }
            else
            {
                listener = new TcpListener(IPADDR, PORT);
                listener.Start();
            }

            while (Active && isThread)
            {
                try
                {
                    if (ConnectionCount <= MAX_CONNECTIONS)
                    {
                        while (!listener.Pending())
                        {
                            Thread.Sleep(500);
                            continue;
                        }

                        TcpClient clientSocket = listener.AcceptTcpClient();

                        HandleClient client = new HandleClient();
                        lock (__ClientHandles)
                            __ClientHandles.Add(Now(), client);

                        client.Start(clientSocket);
                    }
                }
                catch (ThreadAbortException)
                {
                    if (listener != null)
                        listener.Stop();
                    break;
                }
                catch (ThreadInterruptedException)
                {
                    if (listener != null)
                        listener.Stop();
                    break;
                }
                catch (Exception)
                {
                    // TODO: Log it
                }
            }
        }

        private void MaintainClientHandles()
        {
            MaintainClientHandles(true);
        }

        private void MaintainClientHandles(bool isThread)
        {
            if (!isThread && _MaintainingThread == null)
            {
                _MaintainingThread = new Thread(MaintainClientHandles);
                _MaintainingThread.Start();
                return;
            }

            while (Active && isThread)
            {
                try
                {
                    Thread.Sleep(500);

                    // TODO: Wait/Hold when there are no Handles (AutoResetWait)
                    while (__ClientHandles.Count <= 0)
                        Thread.Sleep(500);

                        List<KeyValuePair<int, HandleClient>> tempList;
                        lock (__ClientHandles)
                            tempList = new List<KeyValuePair<int, HandleClient>>(__ClientHandles);

                        foreach (KeyValuePair<int, HandleClient> client in tempList)
                        {
                            if (client.Value.Active == false)
                            {
                                lock (__ClientHandles)
                                    __ClientHandles.Remove(client.Key);
                                client.Value.Dispose();
                            }

                            if (Now() - client.Key > CLIENT_MAX_AGE * 60)
                            {
                                client.Value.Stop();
                                lock (__ClientHandles)                                    
                                    __ClientHandles.Remove(client.Key);
                                client.Value.Dispose();
                            }
                        }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (Exception)
                { }
            }
        }

        // SECONDS
        private static int Now()
        {
            DateTime epochStart = new DateTime(1970, 1, 1);
            DateTime now = DateTime.Now;
            TimeSpan ts = new TimeSpan(now.Ticks - epochStart.Ticks);
            return (Convert.ToInt32(ts.TotalSeconds));
        }
    }
}
