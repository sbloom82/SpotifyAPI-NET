using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SpotifyAPI.AlbumMetadataSearch
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = args[0];
            string[] paths = path.Split(new char[] { Path.DirectorySeparatorChar });

            string artistName = paths[paths.Length - 2];
            string albumName = paths[paths.Length - 1];

            CredentialsAuth auth = new CredentialsAuth("c04cdb49f2e34e25989458ff1653e194", "e8c464ee20d1453aa03330669f4ed2d9");
            Task<Token> task = auth.GetToken();
            task.Wait();

            var api = new SpotifyWebAPI()
            {
                TokenType = "Bearer",
                AccessToken = task.Result.AccessToken
            };

            Console.WriteLine($"Searching for {artistName} - {albumName}");

            List<KeyValuePair<FullAlbum, FullArtist>> albums = new List<KeyValuePair<FullAlbum, FullArtist>>();

            var search = api.SearchItems(artistName, SpotifyAPI.Web.Enums.SearchType.Artist);
            SimpleAlbum exactMatch = null;
            foreach (FullArtist a in search.Artists.Items)
            {
                bool artistDisplayed = false;

                List<SimpleAlbum> searchAlbums = api.GetArtistsAlbums(a.Id, SpotifyAPI.Web.Enums.AlbumType.Album).Items;
                foreach (SimpleAlbum album in searchAlbums)
                {
                    if (a.Name.Equals(artistName, StringComparison.InvariantCultureIgnoreCase) &&
                        album.Name.Equals(albumName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        exactMatch = album;
                        break;
                    }
                }

                foreach (SimpleAlbum album in searchAlbums)
                {
                    if (exactMatch != null && exactMatch != album)
                    {
                        continue;
                    }
                    else if(exactMatch == album)
                    {
                        Console.WriteLine("Exact Match");
                    }

                    if (!artistDisplayed)
                    {
                        Console.WriteLine("--------------------");
                        Console.WriteLine($"Name: {a.Name}");
                        Console.WriteLine($"Id: {a.Id}");
                        Console.WriteLine($"{a.Type} {a.Uri} {a.Href}");

                        Console.WriteLine("   Albums:");
                        artistDisplayed = true;
                    }

                    Console.WriteLine($"#{albums.Count + 1}  {album.Name} - {album.ReleaseDate} - {album.Type}");

                    FullAlbum fullAlbum = api.GetAlbum(album.Id);
                    albums.Add(new KeyValuePair<FullAlbum, FullArtist>(fullAlbum, a));
                    foreach (SimpleTrack track in fullAlbum.Tracks.Items)
                    {
                        Console.WriteLine($"      D:{track.DiscNumber} T:{track.TrackNumber} - {track.Name}, MS:{track.DurationMs}");
                    }
                }
            }

            FullAlbum selectedAlbum = null;
            FullArtist selectedArtist = null;

            if (albums.Count > 0)
            {
                Console.WriteLine("Type album number to accept album info");
                if (int.TryParse(Console.ReadLine(), out int id) && id > 0 && id <= albums.Count)
                {
                    selectedAlbum = albums[id - 1].Key;
                    selectedArtist = albums[id - 1].Value;
                }
            }

            if (selectedAlbum != null)
            {
                string pictureFile = null;
                if (selectedAlbum.Images.Count > 0)
                {                    
                    Console.WriteLine("Downloading album images");
                    List<string> sizes = new List<string>()
                    {
                        "Small",
                        "Large"
                    };

                    foreach (string size in sizes)
                    {
                        Image i = selectedAlbum.Images.OrderBy(im => im.Height).FirstOrDefault(
                            im => size == "Small" ? im.Height <= 300 && im.Height > 64 : im.Height > 300);
                        if (i != null)
                        {
                            try
                            {
                                using (WebClient webClient = new WebClient())
                                {
                                    string name = "AlbumArt_" + size + ".jpg";
                                    if (File.Exists(Path.Combine(path, name)))
                                    {
                                        File.Delete(Path.Combine(path, name));
                                    }
                                    webClient.DownloadFile(i.Url, Path.Combine(path, name));
                                    if (pictureFile == null)
                                    {
                                        pictureFile = Path.Combine(path, name);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error saving thumbnail: " + e.Message);
                            }
                        }
                    }
                }

                int index = 0;
                foreach (string file in Directory.EnumerateFiles(path, "*.mp3").OrderBy(f => f).ToList())
                {
                    SimpleTrack track = selectedAlbum.Tracks.Items[index++];

                    string safeName = track.Name + ".mp3";
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        safeName = safeName.Replace(c, '_');
                    }
                    string moveTo = Path.Combine(Path.GetDirectoryName(file), $"{track.TrackNumber:00} {safeName}");
                    Console.WriteLine($"Updating {Path.GetFileName(file)} to {Path.GetFileName(moveTo)}");
                    try
                    {
                        File.Move(file, moveTo);
                    }
                    catch (Exception e)
                    {

                        Console.WriteLine($"Error: {e.Message}");
                    }

                    TagLib.File tagFile = TagLib.File.Create(moveTo);
                    tagFile.Tag.Title = track.Name;
                    tagFile.Tag.AlbumArtists = new string [] { artistName };
                    tagFile.Tag.Album = selectedAlbum.Name;
                    tagFile.Tag.Track = (uint)track.TrackNumber;
                    tagFile.Tag.Disc = (uint)track.DiscNumber;
                    if (DateTime.TryParse(selectedAlbum.ReleaseDate, out DateTime release))
                    {
                        tagFile.Tag.Year = (uint)release.Year;
                    }
                    tagFile.Tag.Genres = selectedAlbum.Genres.ToArray();
                    tagFile.Tag.Copyright = string.Join(", ", selectedAlbum.Copyrights.Select(c => $"{c.Type} - {c.Text}"));
                    if (pictureFile != null)
                    {
                        tagFile.Tag.Pictures = new TagLib.IPicture[] { new TagLib.Picture(pictureFile) };
                    }
                    tagFile.Save();
                }

                Console.WriteLine("Done");
            }
        }
    }
}