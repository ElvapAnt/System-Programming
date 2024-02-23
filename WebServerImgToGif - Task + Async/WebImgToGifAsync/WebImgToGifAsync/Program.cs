using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Text;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using Image = SixLabors.ImageSharp.Image;
using System.Security.Cryptography;
using System.ComponentModel;
using System.IO.Enumeration;

namespace WebServerImgToGifAsync;

public struct HttpContextTimer
{
    public HttpListenerContext context;
    public Stopwatch timer;
}


class Program
{

    private static object locker = new object();

    private static ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim();

    private static Dictionary<string, byte[]> gifCache = new Dictionary<string, byte[]>();

    private static string gifCachePath = Path.Combine("../../../", "gifCache");

    private static string imagesPath = "../../../images";

    private static void LoadCache()
    {
        if (!Directory.Exists(gifCachePath))
        {
            Directory.CreateDirectory(gifCachePath);
            Console.WriteLine("Cache Folder created successfully.");
        }
        else
        {
            foreach (string gifPath in Directory.GetFiles(gifCachePath))
            {
                string filename = Path.GetFileName(gifPath);
                byte[] image_data = File.ReadAllBytes(gifPath);
                WriteCache(filename, image_data);
            }
            Console.WriteLine("Cache loaded successfully.");
        }
    }
    private static byte[] ReadCache(string filename)
    {
        Console.WriteLine("Cita se kesirana slika...");
        cacheLock.EnterReadLock();
        try
        {
            return gifCache[filename];
        }
        finally
        {
            Console.WriteLine($"Nit uspesno procitala {filename} iz kesa!");
            cacheLock.ExitReadLock();
        }
    }
    private static void WriteCache(string filename, byte[] gif_data)
    {
        Console.WriteLine("Nit upisuje sliku u kesu...");
        cacheLock.EnterWriteLock();
        try
        {
            if (!gifCache.ContainsKey(filename))
            {
                if (gifCache.TryAdd(filename, gif_data))
                {
                    Console.WriteLine("Upisana slika u kesu.");
                }
                else
                {
                    Console.WriteLine("Error!");
                }
            }
            else
            {
                Console.WriteLine("Slika vec postoji, kes je nepromenjen...");
            }
        }
        finally
        {
            Console.WriteLine("Napusta se WriteLock...");
            cacheLock.ExitWriteLock();
        }
    }

    static void Main(string[] args)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5050/");
        listener.Start();
        LoadCache();
        Console.WriteLine("Web server started at port 5050");


        while (listener.IsListening)
        {
            HttpListenerContext context = listener.GetContext();
            HttpContextTimer httpContextTime = new HttpContextTimer();
            httpContextTime.context = context;
            httpContextTime.timer = new Stopwatch();
            httpContextTime.timer.Start();
            ProcessRequest(httpContextTime);
        }

        listener.Stop();
        listener.Close();
    }
    private static void ProcessRequest(HttpContextTimer httpContextTime)
    {
        if (!ThreadPool.QueueUserWorkItem(ProcessRequestExecute, httpContextTime))
        {
            httpContextTime.context.Response.StatusCode = 500;
            HttpResponse("500 - Connection Failed", null, httpContextTime);
        }
    }
    private static async void ProcessRequestExecute(object state)
    {

        byte[] res_data = null;

        HttpContextTimer httpContextTime = (HttpContextTimer)state;

        HttpListenerContext context = httpContextTime.context;

        HttpListenerRequest request = context.Request;
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

        HttpListenerResponse response = context.Response;
        response.ContentType = "image/gif";
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.StatusCode = 200;

        string query = request.Url.AbsolutePath;
        string path = imagesPath + query;
        string filename = query.Substring(1).Replace(".png", ".gif");
        Console.WriteLine($"Request with query : {query}");

        if (query == "/gifCache")
        {
            response.StatusCode = 403;
            await HttpResponse("403 - Access Denied", res_data, httpContextTime);
            return;
        }
        if (File.Exists(path))
        {
            string responseString;
            if (gifCache.ContainsKey(filename))
            {
                response.StatusCode = 202;
                Console.WriteLine($"Cita se postojeci .gif iz kesa {filename}");
                res_data = ReadCache(filename);
                responseString = $"{filename} procitan iz cache memorije";
            }
            else
            {
                Console.WriteLine($"Krece konverzija slike {query}...");
                res_data = await ImageToGif(path, filename);
                responseString = $"{filename} kreiran";
                Console.WriteLine("Konverzija zavrsena, salje se klijentu...");
            }

            await HttpResponse(responseString, res_data, httpContextTime);
            return;
        }
        else
        {
            response.StatusCode = 404;
            await HttpResponse("404 - Not Found", res_data, httpContextTime); ;
            return;
        }
    }
    private static async Task HttpResponse(string responseString, byte[]? res_data, HttpContextTimer httpContextTime)
    {
        HttpListenerResponse res = httpContextTime.context.Response;
        byte[] buffer;
        if (res_data != null)
        {
            buffer = res_data;
            res.ContentLength64 = res_data.Length;
        }
        else
        {
            buffer = Encoding.UTF8.GetBytes(responseString);
            res.ContentLength64 = 64;
        }
        await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        httpContextTime.timer.Stop();

        Console.WriteLine
            ("\n" +
            $"====== Response ====== \n" +
            $"Status code: {res.StatusCode}\n" +
            $"Content type: {res.ContentType}\n" +
            $"Content length: {res.ContentLength64}\n" +
            $"Time taken for response: {httpContextTime.timer.Elapsed.TotalSeconds} s\n" +
            $"Body: {responseString}\n"
            );
    }
    private static async Task<byte[]> ImageToGif(string path, string filename)
    {
        try
        {
            //ucitavanje slike i inicijalizovanje gif-a
            var pngImage = await Image.LoadAsync<Rgba32>(path);
            var gifImage = new Image<Rgba32>(pngImage.Width, pngImage.Height);
            int numOfFrames = 10;
            for (int i = 0; i <= numOfFrames; i++)
            {
                var clone = pngImage.Clone();
                if (i % 2 == 0)
                {
                    clone.Mutate(x => x.Grayscale());
                }
                if (i % 3 == 0)
                {
                    clone.Mutate(x => x.ColorBlindness(ColorBlindnessMode.Deuteranopia));
                }
                if (i % 5 == 0)
                {
                    clone.Mutate(x => x.ColorBlindness(ColorBlindnessMode.Tritanopia));
                }
                gifImage.Frames.AddFrame(clone.Frames[0]);

                gifImage.Frames[gifImage.Frames.Count - 1].Metadata.GetGifMetadata().FrameDelay = 100;
            }

            string gifPath = gifCachePath + "/" + filename;
            //upisivanje u cache folder
            lock (locker)
            {
                if (!gifCache.ContainsKey(filename))
                {
                    Console.WriteLine("Upisuje se u kes folder... ");
                    using (FileStream stream = new FileStream(gifPath, FileMode.Create))
                    {
                        gifImage.SaveAsGif(stream, new GifEncoder { ColorTableMode = GifColorTableMode.Local });
                        Console.WriteLine("Nit uspesno upisala gif u cache folder!");
                    }
                    gifCache[filename] = File.ReadAllBytes(gifPath);
                }
            }
            byte[] gif_data = await File.ReadAllBytesAsync(gifPath);
            return gif_data;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Doslo je do greske prilikom konvertovanja slike sa izuzetkom : {e}");
            return null;
        }
    }
}
