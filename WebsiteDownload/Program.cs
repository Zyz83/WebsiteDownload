using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WebsiteDownload
{
    class Program
    {
        static int downloadCount = 0;
        static string[] startingURL;
        static PageDownloader downloader = null;
        static HashSet<string> matches = new HashSet<string>();
        static void Main(string[] args)
        {
            startingURL = new string[1] { @"<url>" };

            //output location
            var outputDirectory = @"<Path>";
            
            int maxWorkers = 100, maxLinks = 0;
            
            //Create a webpage downloader with as many threads as defined by maxWorkers
            downloader = new PageDownloader(maxWorkers, 3, 60, outputDirectory + "downloadLog.txt");

            Console.WriteLine($"Downloading {(maxLinks != 0 ? $"{maxLinks} links " : string.Empty)}using {maxWorkers} workers...");

            FindPages(startingURL, outputDirectory, maxLinks);
            
            Console.WriteLine($"Downloaded {(maxLinks!=0?$"{maxLinks} links " :string.Empty)}using {maxWorkers} workers");

            Console.ReadKey();
        }



        public static void FindPages(string[] URL, string outputDirectory, int maxDownloadCount)
        {
            var newLinks = new List<string>();

            // Async call to download the URL provided
            downloader.DownloadUrls(URL);

            // Process content in Parallel
            Parallel.ForEach(downloader.downloadsCollection
                .GetConsumingEnumerable(), (webPage, state) =>
            {
                if (webPage.Error == false)
                {
                    // Get all the links from the html
                    if (maxDownloadCount == 0 || downloadCount < maxDownloadCount)
                    {
                        newLinks.AddRange(FindAllLinks(webPage));

                        // Save the downloaded content
                        var fileName = webPage.Url.Replace(startingURL[0], string.Empty);
                        if (fileName.EndsWith("/"))
                            fileName = fileName.Substring(0, fileName.Length - 1);
                        if (fileName.Equals(string.Empty))
                            fileName = "index";
                        else
                            fileName = fileName.Substring(1);
                        var filePath = outputDirectory + fileName;
                        if (!filePath.EndsWith(GetExtension(fileName)))
                            filePath += GetExtension(fileName);
                        filePath = filePath.Replace("/../","/").Replace("/", "\\");
                        if (filePath.Contains("?"))
                            filePath = filePath.Substring(0, filePath.IndexOf("?"));
                        if (!File.Exists(filePath))
                        {
                            Directory.CreateDirectory(filePath.Substring(0, filePath.LastIndexOf("\\")));
                            if (webPage.FileData == null)
                                File.WriteAllText(filePath, webPage.Html);
                            else
                                File.WriteAllBytes(filePath, webPage.FileData);
                            Interlocked.Increment(ref downloadCount);
                        }
                    }
                    else
                    {
                        //we have all the downloads we need...
                        downloader.downloadsCollection.CompleteAdding();
                        //Exit out of the for loop since there still 
                        //may be unused items in the BlockingCollection
                        state.Break();
                    }
                }
            });

            if (newLinks.Count > 0 && (maxDownloadCount == 0 || downloadCount < maxDownloadCount))
                FindPages(newLinks.ToArray(), outputDirectory, maxDownloadCount);
            else
                downloadCount = 0;
        }

        private static string GetExtension(string value)
        {
            return Path.GetExtension(value).Length > 0 ? Path.GetExtension(value) : ".html";
        }

        public static List<string> FindAllLinks(WebPage webPage)
        {
            var file = webPage.Html;
            var list = new List<string>();
            if (file == null)
                return list;

            // Find all matches in file.
            var patterns = new string[] { @"href=\""(?!javascript)(.*?)\""", @"src=\""(.*?)\""", @"content=\""(.*?)\""" };
            var options = RegexOptions.Singleline | RegexOptions.IgnoreCase;
            var m1 = Regex.Matches(file, @"(href=\"".*?\""|src=\"".*?\""|content=\"".*?\"")",
                options);
            
            // Loop over each match.
            foreach (Match m in m1)
            {
                var value = m.Groups[1].Value;
                foreach (var s in patterns)
                {
                    // Get href attribute.
                    var m2 = Regex.Match(value, s, RegexOptions.Singleline);
                    if (m2.Success)
                    {
                        list.AddRange(AddToList(m2, webPage));
                    }
                }
            }
            return list;
        }

        public static List<string> AddToList(Match m, WebPage webPage)
        {
            var result = new List<string>();
            try
            {
                var pageValue = m.Groups[1].Value;

                if (matches.Contains(pageValue) || pageValue.StartsWith("mailto:") || pageValue.StartsWith("tel:"))
                    return result;
                else
                    matches.Add(pageValue);

                var ext = Path.GetExtension(pageValue);

                if (ext.Length >= 4) ext = ext.Substring(0, 4).ToLower();

                if ((ext.Length >= 4 && (ext == ".com" || ext == ".edu" || ext == ".gov")) || (pageValue.Contains("www.google.")))
                    return result;

                if (!pageValue.StartsWith("http"))
                    result.Add(startingURL[0] + (pageValue.StartsWith("/") ? string.Empty : "/") + pageValue);

                // Get only content from the current site
                if (pageValue.StartsWith(webPage.Url))
                    result.Add(pageValue);
            }
            catch
            {
                //just skip if there's an error
            }
            return result;
        }
    }
}
