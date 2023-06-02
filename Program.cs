using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WebServer
{
    class Program
    {
        static readonly string RootDirectory = Directory.GetCurrentDirectory(); 
        static readonly IDictionary<string, string> CachedResponses = new Dictionary<string, string>();
        static readonly ReaderWriterLockSlim CacheLock = new ReaderWriterLockSlim();

        static void Main(string[] args)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5050/");
            listener.Start();

            Console.WriteLine("Web server started");

            while (true)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    Task task = Task.Run(() => ProcessRequestAsync(context));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }


        static async Task ProcessRequestAsync(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;

            string requestUrl = context.Request.Url!.LocalPath;
            string[] requestParams = requestUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);

            string cacheKey = string.Join("&", requestParams);
            //cacheKey.Replace("&", " ");

            string responseString = "";
            CacheLock.EnterReadLock();
            if (CachedResponses.TryGetValue(cacheKey, out string? cachedResponse))
            {

                Console.WriteLine("Cache hit for " + cacheKey + "!");
                Console.WriteLine("Request method: " + context.Request.HttpMethod);
                Console.WriteLine("Request user host address: " + context.Request.UserHostAddress);

                responseString = cachedResponse;
                CacheLock.ExitReadLock();
            }
            else
            {
                CacheLock.ExitReadLock();
                responseString = await GenerateResponseAsync(requestParams);
                CacheLock.EnterWriteLock();
                CachedResponses[cacheKey] = responseString;
                CacheLock.ExitWriteLock();
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();

            Console.WriteLine($"Request {requestUrl} processed successfully");
        }

        static async Task<string> GenerateResponseAsync(string[] requestParams)
        {
            string searchPattern = string.Join("&", requestParams);
            searchPattern = searchPattern.Replace("&", " ");
            string[] words = searchPattern.Split(' ');
            string[] files = Directory.GetFiles(RootDirectory, "*.txt"); //dobavlja sve tekstualne fajlove

            string responseString = "<html><body><h1>Search results: </h1>";

            List<Fajl> filesInRoot = await FetchOccurrences(searchPattern, RootDirectory);

            foreach (Fajl currFajl in filesInRoot)
            {
                responseString += "<p>File name: <strong> " + currFajl.fileName + "</strong></p>";
                foreach (string word in words)
                {
                    int count = 0;
                    string[] fileLines = File.ReadAllLines(currFajl.fileName);
                    foreach (string line in fileLines)
                    {
                        count += line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Count(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase));
                    }
                    if (count > 0)
                    {
                        responseString += "<p style=\"background-color:powderblue;\">Number of word repetitions: " + word + " is " + count + "</p>";
                    }
                    else
                    {
                        responseString += "<p style=\"background-color:red;\">The word " + word + " you searched for does not exist in the file!</p>";
                    }

                }
            }
            responseString += "</body></html>";
            return responseString;
        }

        static async Task<int> ReadLines(string filePath, string findWord)
        {
            int count = 0;

            string tekstfajla = await File.ReadAllTextAsync(filePath); //mozda moze i async verzija

            //pronalazi podudaranja trazene reci u fajlu i vraca broj podudaranja
            count = Regex.Matches(tekstfajla, findWord).Count;

            return count;
        }

        static async Task<List<Fajl>> FetchOccurrences(string searchWord, string root)
        {
            List<Fajl> foundFiles = new List<Fajl>();
            string rootDirectory = @root;
            string searchTerm = searchWord;
            int totalCount = 0;
            string[] words = Regex.Split(searchTerm, @"\W+");

            List<Task<Fajl>> tasks = new List<Task<Fajl>>();

            foreach (string filePath in Directory.EnumerateFiles(rootDirectory, "*.txt", SearchOption.AllDirectories))
            {
                Task<Fajl> task = Task.Run(() =>
                {
                    int count = 0;
                    string fileContents = File.ReadAllText(filePath).ToLower();
                    string[] fileWords = Regex.Split(fileContents, @"\W+");

                    foreach (string word in words)
                    {
                        count += fileWords.Count(w => w == word.ToLower());
                    }

                    Console.WriteLine("{0}: {1} occurrences", filePath, count);

                    if (count > 0)
                    {
                        Fajl currentFile = new Fajl
                        {
                            fileName = Path.GetFileName(filePath),
                            numberOfOccurrences = count
                        };
                        return currentFile;
                    }

                    return null!;
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                Fajl result = await task;
                if (result != null)
                {
                    foundFiles.Add(result);
                    totalCount += result.numberOfOccurrences;
                }
            }

            Console.WriteLine("Total occurrences: {0}", totalCount);
            return foundFiles;
        }

        public class Fajl
        {
            public int numberOfOccurrences;
            public string fileName = "";
        }
    }
}
