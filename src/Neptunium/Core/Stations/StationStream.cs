﻿using System;

namespace Neptunium.Core.Stations
{
    public class StationStream
    {
        public StationStream(Uri url)
        {
            StreamUrl = url;
        }

        //todo remove
        public virtual string SpecificTitle { get { return ParentStation; } }
        public string ParentStation { get; internal set; }
        public Uri StreamUrl { get; private set; }
        public StationStreamServerFormat ServerFormat { get; internal set; }
        public string ContentType { get; internal set; }
        public int Bitrate { get; internal set; }
        public string RelativePath { get; internal set; }
        public bool RequestMetadata { get; internal set; }

        public override string ToString()
        {
            return string.Format("{0} [url: {1} | content-type: {2} | bitrate: {3} | relative-path: {4} | server-format: {5}]", 
                SpecificTitle, 
                StreamUrl?.ToString(), 
                ContentType, 
                Bitrate, 
                RelativePath, 
                Enum.GetName(typeof(StationStreamServerFormat), ServerFormat));
        }
    }
}