﻿using Neptunium.Core.Media.Metadata;
using Neptunium.Media.Songs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.System.Threading;
using static Neptunium.NepApp;

namespace Neptunium.Core
{
    public class NepAppServerFrontEndManager : INotifyPropertyChanged, INepAppFunctionManager
    {
        public const int ServerPortNumber = 8806; //netsh advfirewall firewall add rule name="Open port 8806" dir=in action=allow protocol=TCP localport=8806
        public const int BroadcastPortNumber = 8807; //netsh advfirewall firewall add rule name="Open port 8807" dir=out action=allow protocol=UDP localport=8807

        /// <summary>
        /// From INotifyPropertyChanged
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException("propertyName");

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        public class NepAppServerFrontEndManagerDataReceivedEventArgs : EventArgs
        {
            public string Data { get; private set; }
            internal NepAppServerFrontEndManagerDataReceivedEventArgs(string data)
            {
                Data = data;
            }
        }

        public bool IsInitialized { get; private set; }
        public IEnumerable<IPAddress> LocalEndPoints { get; private set; }

        public event EventHandler<NepAppServerFrontEndManagerDataReceivedEventArgs> DataReceived;

        private List<Tuple<StreamSocket, DataReader, DataWriter>> connections = new List<Tuple<StreamSocket, DataReader, DataWriter>>();
        private StreamSocketListener listener = null; //TCP
        private DatagramSocket serverBroadcaster = null; //UDP

        public async Task InitializeAsync()
        {
            if (IsInitialized) return;

            NepApp.SongManager.PreSongChanged += SongManager_PreSongChanged;

            //set up the tcp listener to listen for connections
            listener = new StreamSocketListener();
            listener.ConnectionReceived += Listener_ConnectionReceived;
            await listener.BindServiceNameAsync(ServerPortNumber.ToString());

            //set up the broadcaster that will broadcast the server's prescence over the network.
            serverBroadcaster = new DatagramSocket();
            await serverBroadcaster.BindServiceNameAsync(BroadcastPortNumber.ToString());

            LocalEndPoints = NepApp.Network.GetLocalIPAddresses();
            RaisePropertyChanged(nameof(LocalEndPoints));

            var broadcastAddr = NepApp.Network.GetBroadastAddress(LocalEndPoints.Last());
            var broadcastDataWriter = new DataWriter(await serverBroadcaster.GetOutputStreamAsync(new HostName(broadcastAddr.ToString()), BroadcastPortNumber.ToString()));

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
            {
                //todo make a way to stop.
                while (true)
                {
                    broadcastDataWriter.WriteString("NEP" + NepAppServerClient.MessageTypeSeperator + LocalEndPoints.First().ToString() + Environment.NewLine);
                    await broadcastDataWriter.StoreAsync();
                    await broadcastDataWriter.FlushAsync();

                    await Task.Delay(30000); //wait 30 seconds
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            IsInitialized = true;
        }

        private async void SongManager_PreSongChanged(object sender, Neptunium.Media.Songs.NepAppSongChangedEventArgs e)
        {
            List<Tuple<StreamSocket, DataReader, DataWriter>> connectionsToRemove = new List<Tuple<StreamSocket, DataReader, DataWriter>>();

            //todo, make an object for this
            foreach (Tuple<StreamSocket, DataReader, DataWriter> tup in connections)
            {
                try
                {
                    await SendMetadataToClientAsync(e.Metadata, tup.Item3);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset
                        || ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionAborted)
                    {
                        connectionsToRemove.Add(tup);
                    }
                }
            }

            foreach (Tuple<StreamSocket, DataReader, DataWriter> tup in connectionsToRemove)
            {
                try
                {
                    tup.Item3.Dispose();
                    tup.Item2.Dispose();
                    tup.Item1.Dispose();
                }
                catch (Exception) { }

                connections.Remove(tup);
            }

            connectionsToRemove.Clear();
        }

        private static async Task SendMetadataToClientAsync(SongMetadata songMetadata, DataWriter dataWriter)
        {
            dataWriter.WriteString("MEDIA" + NepAppServerClient.MessageTypeSeperator + songMetadata.ToString() + Environment.NewLine);
            await dataWriter.StoreAsync();
            await dataWriter.FlushAsync();
        }

        private async void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            DataReader reader = new DataReader(args.Socket.InputStream);
            DataWriter writer = new DataWriter(args.Socket.OutputStream);

            reader.InputStreamOptions = InputStreamOptions.Partial;

            var socketTup = new Tuple<StreamSocket, DataReader, DataWriter>(args.Socket, reader, writer);
            connections.Add(socketTup);

            if (NepApp.SongManager.CurrentSong != null) //send metadata to a freshly connected client.
                await SendMetadataToClientAsync(NepApp.SongManager.CurrentSong, writer);

            while (true)
            {

                string data = "";
                while (!data.EndsWith("\n"))
                {
                    uint available = await reader.LoadAsync(1);

                    if (available > 0)
                    {
                        data += reader.ReadString(available);

                        DataReceived?.Invoke(this, new NepAppServerFrontEndManagerDataReceivedEventArgs(data));

                        ParseDataForCommands(data);
                    }
                    else
                    {
                        lock (connections)
                        {
                            connections.Remove(socketTup);
                            reader.DetachStream();
                            reader.Dispose();
                            writer.DetachStream();
                            writer.Dispose();
                            socketTup.Item1.Dispose();
                        }

                        return;
                    }
                }
            }
        }

        private async void ParseDataForCommands(string data)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                data = data.Trim();
                string[] dataBits = data.Split(NepAppServerFrontEndManager.NepAppServerClient.MessageTypeSeperator);

                if (dataBits.Length > 0)
                {
                    switch (dataBits[0].ToUpper())
                    {
                        case "PLAY":
                            if (dataBits.Length > 1)
                            {
                                string stationName = dataBits[1];
                                var station = await NepApp.Stations.GetStationByNameAsync(stationName);
                                if (station != null)
                                {
                                    var stream = station.Streams.First();
                                    await await App.Dispatcher.RunAsync(async () =>
                                    {
                                        await NepApp.MediaPlayer.TryStreamStationAsync(stream);
                                    });

                                }
                            }

                            break;
                        case "STOP":
                            NepApp.MediaPlayer.Pause();
                            break;
                    }
                }
            }
        }

        private void CleanUp()
        {
            if (!IsInitialized) return;

            //clean up
            listener.Dispose();

            LocalEndPoints = null;
            RaisePropertyChanged(nameof(LocalEndPoints));

            NepApp.SongManager.PreSongChanged -= SongManager_PreSongChanged;

            IsInitialized = false;
        }

        public class NepAppServerClient : IDisposable, INotifyPropertyChanged
        {
            public const char MessageTypeSeperator = '|';

            private StreamSocket tcpClient = null;
            private DataWriter dataWriter = null;
            private DataReader dataReader = null;
            private CancellationTokenSource readerTaskCancellationSource = null;

            public bool IsConnected { get; private set; }

            public NepAppServerClient()
            {
                tcpClient = new StreamSocket();
            }

            public async Task TryConnectAsync(IPAddress address)
            {
                await tcpClient.ConnectAsync(new Windows.Networking.HostName(address.ToString()), ServerPortNumber.ToString());
                dataWriter = new DataWriter(tcpClient.OutputStream);
                dataReader = new DataReader(tcpClient.InputStream);
                readerTaskCancellationSource = new CancellationTokenSource();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(action: ReadDataFromServer, cancellationToken: readerTaskCancellationSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                IsConnected = true;
                RaisePropertyChanged(nameof(IsConnected));
            }

            private async void ReadDataFromServer()
            {
                dataReader.InputStreamOptions = InputStreamOptions.Partial;
                while (!readerTaskCancellationSource.IsCancellationRequested)
                {
                    try
                    {
                        string data = "";
                        while (!data.EndsWith("\n"))
                        {
                            uint available = await dataReader.LoadAsync(1);

                            if (available == 0)
                            {
                                IsConnected = false;
                                RaisePropertyChanged(nameof(IsConnected));
                                Dispose();
                                return;
                            }

                            data += dataReader.ReadString(1);
                        }

                        ParseDataCommand(data);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }

            private void ParseDataCommand(string data)
            {
                if (!string.IsNullOrWhiteSpace(data))
                {
                    data = data.Trim();
                    string[] bits = data.Split(new char[] { MessageTypeSeperator }, 2);

                    switch (bits[0].ToUpper())
                    {
                        case "MEDIA":
                            {
                                //now playing info

                                var metadata = SongMetadata.Parse(bits[1]);
                                SongChanged?.Invoke(this, new NepAppSongChangedEventArgs(metadata));
                                break;
                            }
                    }
                }
            }

            public async void AskServerToStreamStation(Stations.StationItem station)
            {
                if (IsConnected)
                {
                    dataWriter.WriteString("PLAY" + MessageTypeSeperator + station.Name + Environment.NewLine);
                    await dataWriter.StoreAsync();
                    await dataWriter.FlushAsync();
                }
            }

            public async void AskServerToStop()
            {
                if (IsConnected)
                {
                    dataWriter.WriteString("STOP" + MessageTypeSeperator + Environment.NewLine);
                    await dataWriter.StoreAsync();
                    await dataWriter.FlushAsync();
                }
            }

            public event EventHandler<NepAppSongChangedEventArgs> SongChanged;


            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                        if (readerTaskCancellationSource == null) return;
                        if (tcpClient == null) return;

                        readerTaskCancellationSource.Cancel();

                        IsConnected = false;
                        RaisePropertyChanged(nameof(IsConnected));

                        //not proud of this code right here:
                        try
                        {
                            dataWriter.DetachStream();
                        }
                        catch (Exception) { }
                        finally
                        {
                            dataWriter.Dispose();
                        }
                        
                        try
                        {
                            dataReader.DetachStream();
                        }
                        catch (Exception) { }
                        finally
                        {
                            dataReader.Dispose();
                        }
                        tcpClient.Dispose();
                    }

                    // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                    // TODO: set large fields to null.

                    disposedValue = true;
                }
            }

            // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
            // ~NepAppServerClient() {
            //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            //   Dispose(false);
            // }

            // This code added to correctly implement the disposable pattern.
            public void Dispose()
            {
                // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
                Dispose(true);
                // TODO: uncomment the following line if the finalizer is overridden above.
                // GC.SuppressFinalize(this);
            }
            #endregion


            /// <summary>
            /// From INotifyPropertyChanged
            /// </summary>
            public event PropertyChangedEventHandler PropertyChanged;

            private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
            {
                if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException("propertyName");

                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class NepAppServerDiscoverer : IDisposable, INotifyPropertyChanged
        {
            private DatagramSocket udpSocket = new DatagramSocket();

            //todo an event for alerting users of a discovered server. methods for starting and stopping this object.

            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                        udpSocket.Dispose();
                    }

                    // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                    // TODO: set large fields to null.

                    disposedValue = true;
                }
            }

            // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
            // ~NepAppServerDiscoverer() {
            //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            //   Dispose(false);
            // }

            // This code added to correctly implement the disposable pattern.
            public void Dispose()
            {
                // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
                Dispose(true);
                // TODO: uncomment the following line if the finalizer is overridden above.
                // GC.SuppressFinalize(this);
            }
            #endregion
            #region INotifyPropertyChanged Support

            public event PropertyChangedEventHandler PropertyChanged;

            private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
            {
                if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException("propertyName");

                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
            #endregion
        }
    }
}
