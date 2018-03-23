using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebsiteDownload
{
    public class PageDownloader : IDisposable
    {
        public static CancellationToken CancelToken;
        public string LogPath
        {
            get { return this.logPath; }
            set { this.logPath = value; }
        }

        private ConcurrentDictionary<string, int> requeueCountsDict = new ConcurrentDictionary<string, int>();
        public BlockingCollection<WebPage> downloadsCollection;
        private List<Task<WebPage>> downloadTasksList;
        private List<string> retryList = new List<string>();

        private bool log = false;
        private string logPath = "";
        public readonly object lockObject = new object();
        private int maxConcurrentDownloads = 10;
        private int maxRetryCount = 1;
        private int timeoutMs;

        private BlockingCollection<LogMessage> logMessages = new BlockingCollection<LogMessage>();


        public PageDownloader(int MaxConcurrentDownloads = 10, int MaxAttempts = 1, int TimeoutSec = -1, string LogPath = "", bool AppendToLog = false)
        {
            InitCollections();
            Init(MaxConcurrentDownloads, MaxAttempts, TimeoutSec);
            SetLogPath(LogPath, AppendToLog);
        }

        public PageDownloader(int MaxConcurrentDownloads = 10, int MaxAttempts = 1, int TimeoutSec = -1)
        {
            InitCollections();
            Init(MaxConcurrentDownloads, MaxAttempts, TimeoutSec);
        }

        private void Init(int MaxConcurrentDownloads = 10, int MaxAttempts = 1, int TimeoutSec = -1)
        {
            maxConcurrentDownloads = MaxConcurrentDownloads;
            maxRetryCount = MaxAttempts;
            timeoutMs = TimeoutSec * 1000;
        }

        public void SetLogPath(string LogPath = "", bool AppendToLog = false)
        {
            if (LogPath != "" && LogPath != logPath)
            {
                log = true;
                logPath = LogPath;
                if (AppendToLog == false && File.Exists(logPath)) File.Delete(logPath);
                StartLogger();
            }
        }

        private void InitCollections()
        {
            downloadsCollection = new BlockingCollection<WebPage>(new ConcurrentBag<WebPage>());
            CancelToken = new CancellationToken();
            downloadTasksList = new List<Task<WebPage>>();
        }

        public async void DownloadUrls(string[] URLs, bool binary = false)
        {   
            // If this is the new set of dl's (not retrys) We must reset our collections and update the log.
            if (retryList.Count == 0)
            {
                InitCollections();
                if (File.Exists(logPath)) writeToLog("\r\n");
                writeToLog("Downloads Requested: " + URLs.Length + "\r\n" + DateTime.Now + "\r\n" + "###################################################################" + "\r\n");
            }

            int downloadIndex = 0;

            // Create async tasks to download the correct number of URLs concurrently based on 
            // the maxConcurrentDownloads throttle
            int dlCount = URLs.Length < maxConcurrentDownloads ? URLs.Length : maxConcurrentDownloads;
            for (int i = 0; i < dlCount; i++)
            {
                downloadTasksList.Add(DownloadURL(URLs[i], false, CheckBinary(URLs[i])));
                downloadIndex++;
            }

            // Process each download as it completes handling errors
            while (downloadTasksList.Count > 0)
            {
                var nextDownload = await Task.WhenAny(downloadTasksList);
                downloadTasksList.Remove(nextDownload);

                if (downloadIndex < URLs.Length)
                {
                    downloadTasksList.Add(DownloadURL(URLs[downloadIndex], false, CheckBinary(URLs[downloadIndex])));
                    downloadIndex++;
                }

                var download = nextDownload.Result;
                if (download.Error)
                {
                    handleDownloadError(download);
                }
                else
                {
                    writeToLog(download.Url + "|Success" + "\r\n");
                    if (!downloadsCollection.IsAddingCompleted)
                        downloadsCollection.Add(download);
                }
            }

            // Recursively call DownloadUrls until there are no more retries.
            if (retryList.Count > 0)
            {
                DownloadUrls(retryList.ToArray());
                retryList.Clear();
            }
            else
            {
                downloadsCollection.CompleteAdding();
                requeueCountsDict.Clear();
            }
        }

        public async Task<WebPage> DownloadURL(string URL, bool log = false, bool binary = false)
        {
            var client = new HttpClient();
            var successResponse = false;
            if (timeoutMs > 0) client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

            try
            {
                using (client)
                using (var response = await client.GetAsync(URL, CancelToken).ConfigureAwait(false))
                {
                    successResponse = response.IsSuccessStatusCode;
                    
                    // Throw exception for bad response code.
                    response.EnsureSuccessStatusCode();

                    // Download content for good response code.
                    using (var content = response.Content)
                    {
                        // Get the WebPage data.
                        var responseUri = response.RequestMessage.RequestUri.ToString();
                        WebPage WebPage = null;

                        if (binary)
                        {
                            byte[] fileData = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            WebPage = new WebPage(URL, fileData, responseUri);
                        }
                        else
                        {
                            string html = await content.ReadAsStringAsync().ConfigureAwait(false);
                            WebPage = new WebPage(URL, html, responseUri);
                        }

                        // Clean up our attempt tracking
                        int attepmts;
                        requeueCountsDict.TryRemove(URL, out attepmts);
                        if (log) writeToLog(URL + "|Success" + "\r\n");
                        return WebPage;
                    }
                }
            }
            catch (Exception e)
            {
                if (log) writeToLog(URL + "|FAILED|" + e.Message + "\r\n");
                return new WebPage(URL, true, e.Message, successResponse);
            }
        }

        /// <summary>
        /// Create a background task to log any errors and retry downloads up to the max attempts.
        /// </summary>
        /// <param name="download"></param>
        public void handleDownloadError(WebPage download)
        {
            var attempts = requeueCountsDict.AddOrUpdate(download.Url, 1, (key, oldValue) =>
            {
                return oldValue + 1;
            });
            if (attempts < maxRetryCount)
            {
                retryList.Add(download.Url);
            }
            else
            {
                int attemptsWhenRemoved;
                requeueCountsDict.TryRemove(download.Url, out attemptsWhenRemoved);
                writeToLog(download.Url + "|FAILED MAX ATTEMPTS|" + download.ErrorMessage + "\r\n");
            }
        }

        /// <summary>
        /// Converts a relative url path to absolute url path...
        /// </summary>
        /// <param name="Url"></param>
        /// <param name="RelativePath"></param>
        /// <returns></returns>
        public static string ResolveRelativeUrl(string Url, string RelativePath)
        {
            var fullURl = new Uri(new Uri(Url), RelativePath).AbsoluteUri;
            return fullURl;
        }

        /// <summary>
        /// Async Task to write to the log file
        /// </summary>
        /// <param name="Message"></param>
        public void writeToLog(string Message)
        {
            if (log)
            {
                logMessages.Add(new LogMessage(logPath, Message));
            }
        }

        /// <summary>
        /// Dedicates a single background thread to writing log messages so there is no file contention.
        /// </summary>
        public void StartLogger()
        {
            var downloadPages = Task.Factory.StartNew(() =>
            {
                foreach (var logMessage in logMessages.GetConsumingEnumerable())
                {
                    File.AppendAllText(logMessage.Filepath, logMessage.Text);
                }
            });
        }

        public static string getBaseLevelDomain(string URL)
        {
            var uri = new Uri(URL);
            return uri.Host.Replace("www.", "");
        }
        private static bool CheckBinary(string value)
        {
            return Path.GetExtension(value).Length >= 4 && !Path.GetExtension(value).StartsWith(".com");
        }

        public void Dispose()
        {
            logMessages.CompleteAdding();
        }
    }
}
