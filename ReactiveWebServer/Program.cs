#nullable disable
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;
using Octokit.Internal;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Web;

namespace ReactiveWebServer;

/*curl -L \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer <YOUR-TOKEN>"\
  -H "X-GitHub-Api-Version: 2022-11-28" \
  https://api.github.com/search/repositories?q=Q*/

#region ReactiveProgramming
internal class RepositoryStream : IObservable<Repository>
{
    private readonly object locker = new object();
    private ISubject<Repository> repoSubject;
    private readonly IScheduler scheduler;
    public string UserAgent { get; set; }
    public string Token { get; set; }
    public RepositoryStream(string token, string userAgent)
    {
        repoSubject = new Subject<Repository>();
        scheduler = new EventLoopScheduler();
        UserAgent = userAgent;
        Token = token;
    }
    public async Task GetRepositoriesAsync(string language)
    {
        try
        {
            int page = 1;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Action", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Token}");

            while (true)
            {
                HttpResponseMessage response = await client
                    .GetAsync($"https://api.github.com/search/repositories?q=language:{language}&page={page}&per_page=100");
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var apiResponse = System.Text.Json.JsonSerializer.Deserialize<GitHubApiResponse>(jsonResponse, jsonOptions);

                lock (locker)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"The total count of repositories found under the {language} is {apiResponse.TotalCount}");
                    Console.ResetColor();
                }

                foreach (var repo in apiResponse.Items)
                {
                    var repository = new Repository
                    {
                        Id = repo.Id,
                        Name = repo.Name,
                        Owner = repo.Owner
                    };
                    repoSubject.OnNext(repo);
                }

                if (page >= 2)
                {
                    break;
                }

                page++;
            }
            client.Dispose();
            repoSubject.OnCompleted();
        }
        catch (Exception ex)
        {
            //Console.WriteLine(ex.ToString());
            repoSubject.OnError(ex);
        }
    }
    public IDisposable Subscribe(IObserver<Repository> observer)
    {
        return repoSubject.ObserveOn(scheduler).Subscribe(observer);
    }
}
internal class RepositoryObserver : IObserver<Repository>, IObservable<Repository>
{
    private object locker;
    public ConcurrentBag<Task> Tasks;
    private ISubject<Repository> resultSubject;
    private readonly IScheduler scheduler;

    public string Language { get; set; }
    public string UserAgent { get; set; }
    public string Token { get; set; }


    public RepositoryObserver(string language, string userAgent, string token)
    {
        Language = language;
        UserAgent = userAgent;
        Token = token;
        locker = new object();
        Tasks = new ConcurrentBag<Task>();
        resultSubject = new Subject<Repository>();
        scheduler = new EventLoopScheduler();
    }
    private async Task<int> GetCommitCount(string owner, string repoName)
    {
        await Task.Delay(new Random().Next(10, 50));

        string apiUrl = $"https://api.github.com/repos/{owner}/{repoName}/commits?per_page=100";

        HttpClient client = new HttpClient();

        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Token}");

        HttpResponseMessage response = await client.GetAsync(apiUrl);

        if (response.IsSuccessStatusCode)
        {
            string jsonResponse = await response.Content.ReadAsStringAsync();
            var commits = System.Text.Json.JsonSerializer.Deserialize<List<object>>(jsonResponse);
            lock (locker)
            {
                Console.WriteLine($"Author : {owner}");
                Console.WriteLine($"There have been {commits.Count} commits.");
            }
            return commits.Count;
        }
        else
        {
            lock (locker)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error prilikom vracanja broja commit-ova repo-a sa imenom : {repoName}!");
                Console.ResetColor();
            }
            return 0;
        }
    }
    public void OnCompleted()
    {
        Task.WaitAll(Tasks.ToArray());

        lock (locker)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Successfully analyzed all repos under the language {Language}");
            Console.ResetColor();
        }

        resultSubject.OnCompleted();
    }
    public void OnError(Exception error)
    {
        lock (locker)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{Language} error encountered : {error.Message}");
            Console.ResetColor();
        }
        resultSubject.OnError(error);
    }
    public async void OnNext(Repository repo)
    {
        string owner = repo.Owner.Login;
        string repoName = repo.Name;
        int currentTry = 0;
        int maxTries = 3;
        bool success = false;

        while (!success && currentTry < maxTries)
        {
            var task = GetCommitCount(owner, repoName);
            Tasks.Add(task);
            int commitCount = await task;
            if (commitCount > 0)
            {
                success = true;
                repo.CommitCount = commitCount;
                resultSubject.OnNext(repo);
            }

            if (currentTry > 0)
            {
                lock (locker)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"From Attempt : {currentTry}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }

            if (!success)
            {
                currentTry++;

                lock (locker)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"Attempt number : {currentTry}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }
    }
    public IDisposable Subscribe(IObserver<Repository> observer)
    {
        return resultSubject.ObserveOn(scheduler).Subscribe(observer);
    }
}
#endregion

class Program
{
    #region StaticMembers
    private static string port = "http://localhost:5050/";
    private static string authorizationEndpoint = "https://github.com/login/oauth/authorize";
    private static string tokenEndpoint = "https://github.com/login/oauth/access_token";
    private static string redirectUri = "http://localhost:3000";
    private static object locker = new object();
    private static SemaphoreSlim slim = new SemaphoreSlim(1);
    private static GitHubToken token = new GitHubToken();
    private static DateTime? expirationTime = null;
    private static string clientId = "";
    private static string clientSecret = "";
    private static string userAgent = "Reactive GitHub Repo Analyzer";
    private static string scope = "repo";
    private static string code = null;
    #endregion
    
    static void Main(string[] args)
    {
        if (!AuthorizeApp())
        {
            Console.WriteLine("Authorization failed!");
            return;
        }

        HttpListener listener = new HttpListener();
        Console.WriteLine($"Web server started at port 5050");
        ProcessRequest(listener);
    }

    #region ServerLogic
    static bool AuthorizeApp()
    {
        var authorizationUrl = $"{authorizationEndpoint}?client_id={clientId}&redirect_uri={redirectUri}&scope={scope}";
        Console.WriteLine("Please visit the following URL to authorize the application:");
        Console.WriteLine(authorizationUrl);
        Console.WriteLine();
        Console.Write("Enter the authorization code: ");
        code = Console.ReadLine();
        if(code != null)
        {
            return true;
        }
        return false;
    }
    private static async Task GenerateAccessTokenAsync()
    {
        try
        {
            var client = new HttpClient();

            var tokenRequestBody = new Dictionary<string, string>()
            {
                { "code", code },
                { "redirect_uri", redirectUri },
                { "scope", scope }
            };

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));
            client.DefaultRequestHeaders.Add("User-Agent", $"{userAgent}");

            var content = new FormUrlEncodedContent(tokenRequestBody);

            var tokenResponse = await client.PostAsync(tokenEndpoint, content);

            var responseContent = await tokenResponse.Content.ReadAsStringAsync();

            client.Dispose();

            var formData = HttpUtility.ParseQueryString(responseContent);

            token = new GitHubToken()
            {
                access_token = formData["access_token"],
                expires_in = Int32.Parse(formData["expires_in"]),
                refresh_token = formData["refresh_token"],
                refresh_token_expires_in = Int32.Parse(formData["refresh_token_expires_in"]),
                scope = formData["scope"],
                token_type = formData["token_type"]
            };

            expirationTime = DateTime.Now.AddSeconds(token.expires_in);

            lock (locker)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("GitHub access token refreshed");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.StackTrace);
        }
    }
    private static void ProcessRequest(HttpListener listener)
    {
        Thread ServerThread = new Thread(async () =>
        {
            listener.Prefixes.Add(port);
            listener.Start();
            while (listener.IsListening)
            {
                HttpListenerContext context = listener.GetContext();
                //await ProcessRequestExecuteAsync(context);
                Task task = Task.Run(async () =>
                {
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;
                    HttpResponseObserver httpResponse = new HttpResponseObserver();

                    lock (locker)
                    {
                        OutputRequestToConsole(request);
                    }

                    if (request.HttpMethod != "GET")
                    {
                        httpResponse.responseString = $"Invalid request - {request.HttpMethod}";
                        httpResponse.buffer = System.Text.Encoding.UTF8.GetBytes(httpResponse.responseString);
                    }
                    else
                    {
                        response.ContentType = "application/json";
                        response.Headers.Add("Access-Control-Allow-Origin", "*");
                        httpResponse = new HttpResponseObserver();
                        List<string> languages = GetLanguages(request);
                        if (languages != null)
                        {
                            if (expirationTime == null || DateTime.UtcNow >= expirationTime.Value)
                            {
                                await slim.WaitAsync();
                                try
                                {
                                    if (expirationTime == null || DateTime.UtcNow >= expirationTime.Value)
                                        await GenerateAccessTokenAsync();
                                }
                                finally
                                {
                                    slim.Release();
                                }
                            }

                            RepositoryObserver[] repoObservers = new RepositoryObserver[languages.Count];

                            for (int i = 0; i < languages.Count; i++)
                            {
                                repoObservers[i] = new RepositoryObserver(languages[i], userAgent, token.access_token);
                            }


                            var responseObservable = Observable.Merge(repoObservers);
                            var responseSubscription = responseObservable.Subscribe(httpResponse);
                            IDisposable[] subscriptions = new IDisposable[languages.Count];

                            int j = 0;
                            foreach(var repoObs in repoObservers)
                            {
                                Console.WriteLine($"Fetching repositories with language {repoObs.Language}");
                                var repoStream = new RepositoryStream(token.access_token, userAgent);
                                subscriptions[j++] = repoStream.Subscribe(repoObs);
                                await repoStream.GetRepositoriesAsync(repoObs.Language);
                            }

                            await Task.Run(async () =>
                            {
                                while (!httpResponse.IsCompleted)
                                {
                                    await Task.Delay(100);
                                }
                            });

                            responseSubscription.Dispose();
                            foreach (var sub in subscriptions)
                            {
                                sub.Dispose();
                            }
                        }
                        else
                        {
                            httpResponse.responseString = $"Invalid request - language parameter is missing from query";
                            httpResponse.buffer = System.Text.Encoding.UTF8.GetBytes(httpResponse.responseString);
                        }
                    }
                    await HttpResponseAsync(response, httpResponse);

                    lock (locker)
                    {
                        OutputResponseToConsole(response);
                    }
                });
            }
            listener.Stop();
            listener.Close();
        });
        ServerThread.Start();
        ServerThread.Join();
    }
    private static List<string> GetLanguages(HttpListenerRequest request)
    {
        string queryString = request.Url.Query;
        if (queryString == "") return null;
        var queryParams = HttpUtility.ParseQueryString(queryString);
        string languageQuery = queryParams["q"];

        List<string> extractedLanguages = languageQuery
            .Split(new[] { "language:" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(language => language.Trim())
            .ToList();

        return extractedLanguages;
    }
    private static void OutputRequestToConsole(HttpListenerRequest request)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine
            ("\n" +
                $"Request received : {request.Url}\n" +
                $"User host name: {request.UserHostName}\n" +
                $"HTTP method: {request.HttpMethod}\n" +
                //$"HTTP headers: {request.Headers}" +
                $"Content type: {request.ContentType}\n" +
                $"Content length: {request.ContentLength64}\n" +
                $"Cookies: {request.Cookies}\n"
            );
        Console.ResetColor();
    }
    private static void OutputResponseToConsole(HttpListenerResponse response)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine
           ("\n" +
           $"====== Response ====== \n" +
           $"Headers: {response.Headers}\n"+
           $"Status code: {response.StatusCode}\n" +
           $"Content type: {response.ContentType}\n" +
           $"Status description: {response.StatusDescription}\n"+
           $"Content length: {response.ContentLength64}\n"
           );
        Console.ResetColor();
    }
    private static async Task HttpResponseAsync(HttpListenerResponse response, HttpResponseObserver httpResponse)
    { 
        response.ContentLength64 = httpResponse.buffer.Length;
        var output = response.OutputStream;
        await output.WriteAsync(httpResponse.buffer, 0, httpResponse.buffer.Length);
        output.Close();
    }
    #endregion
}

#region HelperClasses
class HttpResponseObserver : IObserver<string>, IObserver<Repository>
{
    public string responseString = "";
    public byte[] buffer;
    public bool IsCompleted { get; private set; }
    public void OnNext(Repository repo)
    {
        responseString = responseString + "\n" + repo.ToString() + "\n";
    }
    public void OnNext(string value)
    {
        responseString = responseString + value + "\n";
    }
    public void OnCompleted()
    {
        if (responseString.Length == 0)
        {
            responseString = "No repositories found.";
        }
        buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        IsCompleted = true;
    }
    public void OnError(Exception error)
    {
        responseString = responseString + error.Message + "\n";
        buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        IsCompleted = true;
    }
}
class GitHubToken
{
    public string access_token { get; set; }
    public int expires_in { get; set; }
    public string refresh_token { get; set; }
    public int refresh_token_expires_in { get; set; }
    public string scope { get; set; }
    public string token_type { get; set; }
}
public class Repository
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("owner")]
    public Owner Owner { get; set; }
    public int CommitCount { get; set; }
    public override string ToString()
    {
        return $"Owner : {Owner.Login}\n" +
               $"Number of commits: {CommitCount}\n";
    }
}
public class Owner
{
    [JsonPropertyName("login")]
    public string Login { get; set; }
}
public class GitHubApiResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
    [JsonPropertyName("items")]
    public List<Repository> Items { get; set; }
}
#endregion





