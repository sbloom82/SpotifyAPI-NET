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

            int depth = 0;
            DirectoryInfo dir = new DirectoryInfo(path);
            DirectoryInfo[] children = dir.GetDirectories();
            while (children.Length > 0)
            {
                depth++;
                children = children[0].GetDirectories();
            }

            if (depth > 0)
            {
                Console.WriteLine($"Child directories found, detected depth is {depth}.  Press Y to continue");
                if (!Console.ReadLine().Trim().Equals("Y", StringComparison.InvariantCultureIgnoreCase))
                {
                    return;
                }
                Console.WriteLine();
            }

            CredentialsAuth auth = new CredentialsAuth("c04cdb49f2e34e25989458ff1653e194", "e8c464ee20d1453aa03330669f4ed2d9");
            Task<Token> task = auth.GetToken();
            task.Wait();

            var api = new SpotifyWebAPI()
            {
                TokenType = "Bearer",
                AccessToken = task.Result.AccessToken
            };

            ProcessDirectory(api, dir, depth);
        }

        private static SearchItem artistSearch = null;
        public static SearchItem GetArtistSearch(SpotifyWebAPI api, string artistName)
        {
            if (artistSearch == null)
            {
                artistSearch = api.SearchItems(artistName, Web.Enums.SearchType.Artist, market: "US");
            }
            return artistSearch;
        }

        public static Dictionary<string, List<SimpleAlbum>> artistAlbums = new Dictionary<string, List<SimpleAlbum>>(StringComparer.InvariantCultureIgnoreCase);
        public static List<SimpleAlbum> GetArtistAlbums(SpotifyWebAPI api, string id)
        {
            if (!artistAlbums.TryGetValue(id, out List<SimpleAlbum> albums))
            {
                albums = api.GetArtistsAlbums(id, SpotifyAPI.Web.Enums.AlbumType.Album, limit: 250, market: "US").Items;
                artistAlbums[id] = albums;
            }
            return albums;
        }

        public static Dictionary<string, FullAlbum> fullAlbums = new Dictionary<string, FullAlbum>();
        public static FullAlbum GetFullAlbum(SpotifyWebAPI api, string id)
        {
            if (!fullAlbums.TryGetValue(id, out FullAlbum fullAlbum))
            {
                fullAlbum = api.GetAlbum(id);
            }
            return fullAlbum;
        }

        public static void ProcessDirectory(SpotifyWebAPI api, DirectoryInfo dir, int depth)
        {
            if (depth != 0)
            {
                depth--;
                foreach (DirectoryInfo di in dir.GetDirectories())
                {
                    ProcessDirectory(api, di, depth);
                }
                return;
            }

            string path = dir.FullName;
            string[] paths = path.Split(new char[] { Path.DirectorySeparatorChar });

            string artistName = paths[paths.Length - 2];
            string albumName = paths[paths.Length - 1];

            Console.WriteLine($"Searching for {artistName} - {albumName}");

            List<KeyValuePair<FullAlbum, FullArtist>> albums = new List<KeyValuePair<FullAlbum, FullArtist>>();

            var search = GetArtistSearch(api, artistName);

            bool hasExactMatch = false;
            bool hasQualifyingMatch = false;
            foreach (FullArtist a in search.Artists.Items)
            {
                List<SimpleAlbum> searchAlbums = GetArtistAlbums(api, a.Id);
                if (searchAlbums == null)
                {
                    continue;
                }

                foreach (SimpleAlbum album in searchAlbums)
                {
                    if (a.Name.Equals(artistName, StringComparison.InvariantCultureIgnoreCase) &&
                        album.Name.Equals(albumName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        hasExactMatch = true;
                        break;
                    }
                    else if (
                        (artistName.StartsWith(a.Name, StringComparison.InvariantCultureIgnoreCase) || a.Name.StartsWith(artistName, StringComparison.InvariantCultureIgnoreCase)) &&
                        (albumName.StartsWith(album.Name, StringComparison.InvariantCultureIgnoreCase) || album.Name.StartsWith(albumName, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        hasQualifyingMatch = true;
                    }
                }

                foreach (SimpleAlbum album in searchAlbums)
                {
                    bool exactMatch =
                        a.Name.Equals(artistName, StringComparison.InvariantCultureIgnoreCase) &&
                        album.Name.Equals(albumName, StringComparison.InvariantCultureIgnoreCase);

                    bool qualifyingMatch =
                        (artistName.StartsWith(a.Name, StringComparison.InvariantCultureIgnoreCase) || a.Name.StartsWith(artistName, StringComparison.InvariantCultureIgnoreCase)) &&
                        (albumName.StartsWith(album.Name, StringComparison.InvariantCultureIgnoreCase) || album.Name.StartsWith(albumName, StringComparison.InvariantCultureIgnoreCase));

                    if (hasExactMatch)
                    {
                        if (!exactMatch)
                        {
                            continue;
                        }
                        Console.WriteLine("Exact Match");
                    }
                    else if (hasQualifyingMatch)
                    {
                        if (!qualifyingMatch)
                        {
                            continue;
                        }
                        Console.WriteLine("Qualifying Match");
                    }

                    Console.WriteLine($"#{albums.Count + 1} - {a.Name} - \"{album.Name}\" - {album.ReleaseDate} - {album.Type}");

                    FullAlbum fullAlbum = GetFullAlbum(api, album.Id);
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

                int index = -1;
                foreach (string file in Directory.EnumerateFiles(path, "*.mp3").OrderBy(f => f).ToList())
                {
                    if (selectedAlbum.Tracks.Items.Count > ++index)
                    {
                        SimpleTrack track = selectedAlbum.Tracks.Items[index];

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
                        tagFile.Tag.Performers = track.Artists.Select(a => a.Name).ToArray();
                        tagFile.Tag.AlbumArtists = new string[] { artistName };
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
                    else
                    {
                        Console.WriteLine($"{Path.GetFileName(file)} not in track listing");
                    }
                }

                Console.WriteLine($"{artistName} - {albumName} Complete");
                Console.WriteLine("-------------------------------------");
                Console.WriteLine();
                //Console.Clear();
            }
        }
    }
}