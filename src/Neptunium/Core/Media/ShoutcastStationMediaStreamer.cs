﻿using System;
using System.Threading.Tasks;
using Neptunium.Core.Stations;
using Windows.Media.Playback;
using Windows.Media.Core;
using UWPShoutcastMSS.Streaming;

namespace Neptunium.Media
{
    internal class ShoutcastStationMediaStreamer : BasicNepAppMediaStreamer
    {
        private ShoutcastStream streamSource = null;
        public override void InitializePlayback(MediaPlayer player)
        {
            Player = player;
            Player.Source = StreamMediaSource;
            this.IsPlaying = true;
        }

        public override async Task TryConnectAsync(StationStream stream)
        {      
            try
            {
                streamSource = await ShoutcastStreamFactory.ConnectAsync(stream.StreamUrl, new ShoutcastStreamFactoryConnectionSettings()
                {
                    UserAgent = "Neptunium (http://github.com/Amrykid/Neptunium)",
                    RelativePath = stream.RelativePath,
                    RequestSongMetdata = stream.RequestMetadata
                });

                streamSource.Reconnected += StreamSource_Reconnected;
                streamSource.MetadataChanged += ShoutcastStream_MetadataChanged;
                StreamMediaSource = MediaSource.CreateFromMediaStreamSource(streamSource.MediaStreamSource);
                StreamMediaSource.StateChanged += StreamMediaSource_StateChanged;
                this.StationPlaying = await NepApp.Stations.GetStationByNameAsync(stream.ParentStation);

                ShoutcastStationInfo = streamSource.StationInfo;
                RaiseStationInfoAcquired(streamSource.StationInfo);
            }
            catch (Exception ex)
            {
                if (ex is System.Runtime.InteropServices.COMException)
                {
                    if (ex.HResult == -2147014836)
                    {
                        /* A connection attempt failed because the connected party did not properly respond after a period of time, or 
                           established connection failed because connected host has failed to respond. */

                        //At this point, we've already timed out and informed the user. We can use return out of this.

                        return;
                    }
                }

                throw new Neptunium.Core.NeptuniumStreamConnectionFailedException(stream, ex);
            }
        }

        public ServerStationInfo ShoutcastStationInfo { get; private set; }

        public override bool PollConnection()
        {
            if (streamSource == null) return false;

            return streamSource.PollConnection();
        }

        private void StreamMediaSource_StateChanged(MediaSource sender, MediaSourceStateChangedEventArgs args)
        {

        }

        private void StreamSource_Reconnected(object sender, EventArgs e)
        {
            
        }

        private void ShoutcastStream_MetadataChanged(object sender, ShoutcastMediaSourceStreamMetadataChangedEventArgs e)
        {
            RaiseMetadataChanged(new Core.Media.Metadata.SongMetadata()
            {
                Artist = e.Artist,
                Track = e.Title,
                StationPlayedOn = this.StationPlaying.Name,
                StationLogo = this.StationPlaying.StationLogoUrl
            });
        }

        public override void Dispose()
        {
            if (streamSource != null)
            {
                streamSource.Disconnect();
                streamSource.Reconnected -= StreamSource_Reconnected;
                streamSource.MetadataChanged -= ShoutcastStream_MetadataChanged;
            }

            if (StreamMediaSource != null)
            {
                StreamMediaSource.StateChanged -= StreamMediaSource_StateChanged;
            }

            base.Dispose();
        }
    }
}