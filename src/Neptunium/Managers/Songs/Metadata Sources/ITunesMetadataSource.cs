﻿using Neptunium.Managers.Songs.Metadata_Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Neptunium.Data;
using iTunesSearch.Library.Models;

namespace Neptunium.Managers.Songs
{
    public class ITunesMetadataSource : ISongMetadataSource
    {
        iTunesSearch.Library.iTunesSearchManager itunesStore = null;
        public ITunesMetadataSource()
        {
            itunesStore = new iTunesSearch.Library.iTunesSearchManager();
        }
        public Task<ArtistData> GetArtistAsync(string artistID)
        {
            throw new NotImplementedException();
        }

        public async Task<AlbumData> TryFindAlbumAsync(string track, string artist)
        {
            //todo figure out a better way to do this. maybe romanize the japanese artist names for better accuracy?
            var albums = await itunesStore.SearchAlbumsAsync(string.Join(" ", artist, track), 5, "jp");
            if (albums.Count == 0)
                albums = await itunesStore.GetAlbumsFromSongAsync(track, 5, "jp");

            if (albums.Count > 0)
            {
                Album selectedAlbum = null;

                selectedAlbum = albums.Albums.FirstOrDefault(x => x.ArtistName.Trim().Equals(artist.Trim()));
                if (selectedAlbum == null)
                    selectedAlbum = albums.Albums.First(); //give up until we figure out a better way to do this.

                var data = new AlbumData();

                data.Album = selectedAlbum.CollectionName;
                data.AlbumID = selectedAlbum.CollectionId.ToString();

                if (!string.IsNullOrWhiteSpace(selectedAlbum.ArtworkUrl100))
                    data.AlbumCoverUrl = selectedAlbum.ArtworkUrl100.Replace("100x100", "600x600");
                else if (!string.IsNullOrWhiteSpace(selectedAlbum.ArtworkUrl60))
                    data.AlbumCoverUrl = selectedAlbum.ArtworkUrl60.Replace("100x100", "600x600");

                data.AlbumLinkUrl = selectedAlbum.CollectionViewUrl;
                data.Artist = selectedAlbum.ArtistName;
                data.ArtistID = selectedAlbum.ArtistId.ToString();
                //data.ReleaseDate = selectedAlbum.ReleaseDate;

                return data;
            }

            return null;
        }

        public async Task<ArtistData> TryFindArtistAsync(string artistName)
        {
            var artists = await itunesStore.GetSongArtistsAsync(artistName, 5, "jp");

            if (artists.Count > 0)
            {
                var selectedArtist = artists.Artists.First();

                var data = new ArtistData();
                data.Name = selectedArtist.ArtistName;
                data.ArtistID = selectedArtist.ArtistId.ToString();
                data.ArtistLinkUrl = selectedArtist.ArtistLinkUrl;

                return data;
            }

            return null;
        }
    }
}
